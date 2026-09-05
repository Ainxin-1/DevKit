using Microsoft.Win32;

namespace DevEnvManager.Core;

/// <summary>
/// PATH 管理：仅刷新当前进程环境（不写注册表、不改系统设置）。
/// 安全原则：不偷偷修改 PATH；安装后刷新当前进程 PATH，使复检能立即找到新工具。
/// </summary>
public static class PathManager
{
    /// <summary>
    /// 从注册表读取用户级 + 系统级 PATH，合并进当前进程环境。
    /// 返回新增的 PATH 条目数（供日志展示）。
    /// </summary>
    public static int RefreshCurrentProcessPath()
    {
        try
        {
            var userPath = ReadRegistryPath(Registry.CurrentUser, @"Environment", "Path");
            var machinePath = ReadRegistryPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Path");
            var merged = Merge(userPath, machinePath);

            var current = Environment.GetEnvironmentVariable("PATH") ?? "";
            var currentSet = new HashSet<string>(
                current.Split(';', StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var entry in merged)
            {
                if (!currentSet.Contains(entry))
                {
                    // 仅当目录存在时才加入当前进程 PATH，避免污染
                    if (System.IO.Directory.Exists(entry))
                    {
                        Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + entry);
                        added++;
                    }
                }
            }
            Logger.Info($"刷新当前进程 PATH：新增 {added} 个有效目录");
            return added;
        }
        catch (Exception ex)
        {
            Logger.Warn($"PATH 刷新失败（不影响主流程）: {ex.Message}");
            return 0;
        }
    }

    private static string? ReadRegistryPath(RegistryKey hive, string subKey, string name)
    {
        using var key = hive.OpenSubKey(subKey);
        return key?.GetValue(name) as string;
    }

    private static List<string> Merge(params string?[] paths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            foreach (var entry in p.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(entry.Trim());
            }
        }
        return set.ToList();
    }
}
