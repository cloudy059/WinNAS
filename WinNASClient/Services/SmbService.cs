using System.Runtime.InteropServices;
using Serilog;

namespace WinNASClient.Services;

public class SmbService
{
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareAdd(string servername, int level, ref SHARE_INFO_2 buf, out int parm_err);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareDel(string servername, string netname, int reserved);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string servername, int level, out IntPtr bufptr, int prefmaxlen, out int entriesread, out int totalentries, out int resume_handle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHARE_INFO_2
    {
        public string shi2_netname;
        public int shi2_type;
        public string shi2_remark;
        public int shi2_permissions;
        public int shi2_max_uses;
        public int shi2_current_uses;
        public string shi2_path;
        public string shi2_passwd;
    }

    private const int STYPE_DISKTREE = 0;
    private const int ACCESS_ALL = 0;
    private const int NERR_Success = 0;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int NERR_DuplicateShare = 2118;
    private const int MAX_PREFERRED_LENGTH = -1;

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var root = System.IO.Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return root.TrimEnd('\\');
        return path.TrimEnd('\\');
    }

    public async Task<(bool Success, string Message)> ShareDirectoryAsync(string name, string path, string remark = "")
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(path))
                return (false, $"目录不存在: {path}");

            var shareName = SanitizeName(name);

            var shareInfo = new SHARE_INFO_2
            {
                shi2_netname = shareName,
                shi2_type = STYPE_DISKTREE,
                shi2_remark = string.IsNullOrEmpty(remark) ? "WinNAS Shared Folder" : remark,
                shi2_permissions = ACCESS_ALL,
                shi2_max_uses = -1,
                shi2_current_uses = 0,
                shi2_path = path,
                shi2_passwd = null!
            };

            int result = NetShareAdd(null, 2, ref shareInfo, out int parmErr);

            if (result == NERR_Success)
            {
                Log.Information("SMB share created: {ShareName} -> {Path}", shareName, path);
                return (true, $"SMB共享已创建: \\\\{GetLocalIP()}\\{shareName}");
            }

            if (result == ERROR_ACCESS_DENIED)
                return (false, "需要管理员权限才能创建SMB共享，请以管理员身份运行WinNAS");

            if (result == NERR_DuplicateShare)
            {
                return UpdateExistingShare(shareName, path, remark);
            }

            return (false, $"创建SMB共享失败 (错误码: {result})");
        });
    }

    private (bool Success, string Message) UpdateExistingShare(string shareName, string path, string remark)
    {
        int delResult = NetShareDel(null, shareName, 0);
        if (delResult != NERR_Success && delResult != NERR_DuplicateShare)
            return (false, $"更新SMB共享失败: 无法删除旧共享 (错误码: {delResult})");

        var shareInfo = new SHARE_INFO_2
        {
            shi2_netname = shareName,
            shi2_type = STYPE_DISKTREE,
            shi2_remark = string.IsNullOrEmpty(remark) ? "WinNAS Shared Folder" : remark,
            shi2_permissions = ACCESS_ALL,
            shi2_max_uses = -1,
            shi2_current_uses = 0,
            shi2_path = path,
            shi2_passwd = null!
        };

        int result = NetShareAdd(null, 2, ref shareInfo, out _);
        if (result == NERR_Success)
            return (true, $"SMB共享已更新: \\\\{GetLocalIP()}\\{shareName}");

        return (false, $"更新SMB共享失败 (错误码: {result})");
    }

    public async Task<(bool Success, string Message)> UnshareDirectoryAsync(string name)
    {
        return await Task.Run(() =>
        {
            var shareName = SanitizeName(name);
            int result = NetShareDel(null, shareName, 0);

            if (result == NERR_Success)
            {
                Log.Information("SMB share removed: {ShareName}", shareName);
                return (true, $"SMB共享已移除: {shareName}");
            }

            return (false, $"移除SMB共享失败 (错误码: {result})");
        });
    }

    public async Task<List<SmbShareInfo>> GetWinNASSharesAsync(IEnumerable<string>? knownPaths = null)
    {
        return await Task.Run(() =>
        {
            var shares = new List<SmbShareInfo>();
            int entriesRead, totalEntries, resumeHandle = 0;

            int result = NetShareEnum(null, 2, out IntPtr bufPtr, MAX_PREFERRED_LENGTH,
                out entriesRead, out totalEntries, out resumeHandle);

            if (result != NERR_Success)
                return shares;

            var normalizedKnownPaths = knownPaths?
                .Select(p => NormalizePath(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            try
            {
                int structSize = Marshal.SizeOf<SHARE_INFO_2>();
                IntPtr current = bufPtr;

                for (int i = 0; i < entriesRead; i++)
                {
                    var shareInfo = Marshal.PtrToStructure<SHARE_INFO_2>(current);
                    current += structSize;

                    if (shareInfo.shi2_type != STYPE_DISKTREE)
                        continue;

                    if (shareInfo.shi2_netname.EndsWith("$"))
                        continue;

                    if (normalizedKnownPaths != null)
                    {
                        var sharePath = NormalizePath(shareInfo.shi2_path ?? "");
                        if (!normalizedKnownPaths.Contains(sharePath))
                            continue;
                    }

                    shares.Add(new SmbShareInfo
                    {
                        ShareName = shareInfo.shi2_netname,
                        Path = shareInfo.shi2_path,
                        Remark = shareInfo.shi2_remark,
                        CurrentUses = shareInfo.shi2_current_uses
                    });
                }
            }
            finally
            {
                NetApiBufferFree(bufPtr);
            }

            return shares;
        });
    }

    public async Task<(bool Success, string Message)> UnshareAllAsync(IEnumerable<string>? knownPaths = null)
    {
        var shares = await GetWinNASSharesAsync(knownPaths);
        var messages = new List<string>();
        bool allSuccess = true;

        foreach (var share in shares)
        {
            int result = NetShareDel(null, share.ShareName, 0);
            if (result != NERR_Success)
            {
                allSuccess = false;
                messages.Add($"移除 {share.ShareName} 失败");
            }
            else
            {
                messages.Add($"移除 {share.ShareName} 成功");
            }
        }

        return (allSuccess, string.Join("; ", messages));
    }

    public async Task<(bool Success, string Message)> SyncSharesAsync(IEnumerable<Models.SharedDirectory> directories)
    {
        var dirPaths = directories.Select(d => d.Path);
        var existingShares = await GetWinNASSharesAsync(dirPaths);
        var existingPaths = existingShares.Select(s => NormalizePath(s.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedDirPaths = directories.Select(d => NormalizePath(d.Path)).ToList();

        var messages = new List<string>();

        foreach (var dir in directories)
        {
            if (!existingPaths.Contains(NormalizePath(dir.Path)))
            {
                var (ok, msg) = await ShareDirectoryAsync(dir.Name, dir.Path, dir.Description);
                messages.Add(msg);
            }
        }

        foreach (var share in existingShares)
        {
            if (!normalizedDirPaths.Contains(NormalizePath(share.Path)))
            {
                int result = NetShareDel(null, share.ShareName, 0);
                if (result == NERR_Success)
                    messages.Add($"移除过期共享: {share.ShareName}");
            }
        }

        if (messages.Count == 0)
            messages.Add("所有共享已同步，无需更新");

        return (true, string.Join("\n", messages));
    }

    private static string SanitizeName(string name)
    {
        var chars = new List<char>();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                chars.Add(c);
            else if (c == ' ')
                chars.Add('_');
        }
        var result = new string(chars.ToArray());

        if (result.Length > 0 && char.IsDigit(result[0]))
            result = "Share_" + result;

        return result;
    }

    private static string GetLocalIP()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }
}

public class SmbShareInfo
{
    public string ShareName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Remark { get; set; } = "";
    public int CurrentUses { get; set; }
}
