using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using WinNASClient.Config;
using WinNASClient.Services;

namespace WinNASClient.HttpServer.Controllers;

public class DiscoveryController
{
    private readonly DiscoveryService _discoveryService;
    private readonly FileService _fileService;
    private readonly AppConfig _config;
    private readonly HttpClient _httpClient;

    public DiscoveryController(DiscoveryService discoveryService, FileService fileService, AppConfig config)
    {
        _discoveryService = discoveryService;
        _fileService = fileService;
        _config = config;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void Register(WebApplication app)
    {
        app.MapGet("/api/discovery/peers", () =>
        {
            var peers = _discoveryService.GetPeers();
            return Results.Ok(new { success = true, data = peers });
        });

        app.MapGet("/api/discovery/info", () =>
        {
            var localIP = WinNASClient.Utils.NetworkHelper.GetLocalIpAddress();
            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    ip = localIP,
                    port = _config.HttpPort,
                    machineName = Environment.MachineName,
                    allowGuest = _config.AllowGuest
                }
            });
        });

        app.MapGet("/api/discovery/public-directories", () =>
        {
            var dirs = _fileService.GetAllDirectoriesAsync().GetAwaiter().GetResult();
            var publicDirs = dirs.Where(d => d.Visibility == "public").Select(d => new
            {
                d.Id, d.Name, d.Description, d.Visibility,
                d.AllowUpload, d.AllowDelete, d.AllowRename, d.AllowMove
            });
            return Results.Ok(new { success = true, data = publicDirs });
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/info", async (string ip, int port) =>
        {
            try
            {
                var url = $"http://{ip}:{port}/api/discovery/info";
                var response = await _httpClient.GetFromJsonAsync<PeerInfoResponse>(url);
                return Results.Ok(new { success = true, data = response?.Data });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"无法连接到 {ip}:{port}: {ex.Message}" });
            }
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/directories", async (string ip, int port) =>
        {
            try
            {
                var url = $"http://{ip}:{port}/api/discovery/public-directories";
                var response = await _httpClient.GetFromJsonAsync<PeerDirectoriesResponse>(url);
                return Results.Ok(new { success = true, data = response?.Data });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"无法获取目录列表: {ex.Message}" });
            }
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/files", async (string ip, int port, int dirId, string? path) =>
        {
            try
            {
                var url = $"http://{ip}:{port}/api/guest/files?dirId={dirId}";
                if (!string.IsNullOrEmpty(path)) url += $"&path={Uri.EscapeDataString(path)}";
                var response = await _httpClient.GetFromJsonAsync<PeerFilesResponse>(url);
                return Results.Ok(new { success = true, data = response?.Data, dirPath = response?.DirPath });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = $"无法获取文件列表: {ex.Message}" });
            }
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/download", (string ip, int port, int dirId, string? path) =>
        {
            var url = $"http://{ip}:{port}/api/guest/download?dirId={dirId}";
            if (!string.IsNullOrEmpty(path)) url += $"&path={Uri.EscapeDataString(path)}";
            return Results.Redirect(url);
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/preview", (string ip, int port, int dirId, string? path) =>
        {
            var url = $"http://{ip}:{port}/api/guest/preview?dirId={dirId}";
            if (!string.IsNullOrEmpty(path)) url += $"&path={Uri.EscapeDataString(path)}";
            return Results.Redirect(url);
        });

        app.MapGet("/api/discovery/peer/{ip}/{port}/stream", (string ip, int port, int dirId, string? path) =>
        {
            var url = $"http://{ip}:{port}/api/guest/stream?dirId={dirId}";
            if (!string.IsNullOrEmpty(path)) url += $"&path={Uri.EscapeDataString(path)}";
            return Results.Redirect(url);
        });
    }
}

public class PeerInfoResponse
{
    public bool Success { get; set; }
    public PeerDetail? Data { get; set; }
}

public class PeerDetail
{
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public string MachineName { get; set; } = "";
    public bool AllowGuest { get; set; }
}

public class PeerDirectoriesResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
}

public class PeerFilesResponse
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? DirPath { get; set; }
}
