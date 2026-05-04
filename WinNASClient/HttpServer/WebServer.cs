using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WinNASClient.Config;
using WinNASClient.Database;
using WinNASClient.HttpServer.Controllers;
using WinNASClient.Services;

namespace WinNASClient.HttpServer;

public class WebServer
{
    private readonly AppConfig _config;
    private WebApplication? _app;
    private readonly DbInitializer _dbInitializer;
    private readonly DbRepository _repo;
    private readonly AuthService _authService;
    private readonly FileService _fileService;
    private readonly ShareService _shareService;
    private readonly TeamService _teamService;
    private readonly SystemService _systemService;
    private readonly SmbService _smbService;
    private readonly DiscoveryService _discoveryService;

    public WebServer(AppConfig config)
    {
        _config = config;
        _dbInitializer = new DbInitializer(config.DbPath);
        _repo = new DbRepository(_dbInitializer);
        _authService = new AuthService(_repo, config.JwtSecret, config.JwtExpireHours);
        _fileService = new FileService(_repo, config.DuplicateCheck);
        _shareService = new ShareService(_repo);
        _teamService = new TeamService(_repo);
        _systemService = new SystemService(_repo);
        _smbService = new SmbService();
        _discoveryService = new DiscoveryService(config);
    }

    public DbInitializer Database => _dbInitializer;
    public AuthService Auth => _authService;
    public FileService FileSvc => _fileService;
    public ShareService Share => _shareService;
    public TeamService Team => _teamService;
    public SystemService System => _systemService;
    public SmbService Smb => _smbService;
    public DiscoveryService Discovery => _discoveryService;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_config.HttpPort}");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = _config.MaxUploadSize;
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = _config.MaxUploadSize;
        });

        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.PropertyNamingPolicy = global::System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.JwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        _app = builder.Build();

        _app.UseCors();

        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/webdav"))
            {
                context.Items["SkipAuth"] = true;
            }
            await next();
        });

        _app.UseMiddleware<WebDavMiddleware>(_repo, _authService);

        _app.UseAuthentication();
        _app.UseAuthorization();

        if (Directory.Exists(_config.WebRootPath))
        {
            _app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new PhysicalFileProvider(_config.WebRootPath),
                RequestPath = ""
            });
            _app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(_config.WebRootPath),
                RequestPath = "",
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream"
            });
        }

        RegisterRoutes();

        _discoveryService.Start();

        _app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(_config.WebRootPath)
        });

        await _app.StartAsync(cancellationToken);
    }

    private void RegisterRoutes()
    {
        var authController = new AuthController(_authService, _systemService, _config);
        var fileController = new FileController(_fileService, _shareService, _systemService, _authService, _config, _repo);
        var shareController = new ShareController(_shareService, _fileService, _systemService);
        var teamController = new TeamController(_teamService, _systemService, _authService, _config);
        var systemController = new SystemController(_systemService, _fileService, _shareService, _teamService, _authService, _config);
        var networkShareController = new NetworkShareController(_smbService, _fileService, _authService, _config);
        var discoveryController = new DiscoveryController(_discoveryService, _fileService, _config);
        var enhancedController = new EnhancedController(_repo, _fileService, _systemService, _authService, _config);

        authController.Register(_app!);
        fileController.Register(_app!);
        shareController.Register(_app!);
        teamController.Register(_app!);
        systemController.Register(_app!);
        networkShareController.Register(_app!);
        discoveryController.Register(_app!);
        enhancedController.Register(_app!);
    }

    public async Task StopAsync()
    {
        _discoveryService.Stop();
        if (_app != null)
        {
            await _app.StopAsync();
        }
    }
}
