using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DevKit.Core;

/// <summary>系统信息条目（UI 展示用）</summary>
public class SystemInfoItem
{
    public required string Key { get; init; }
    public required string Value { get; init; }
}

/// <summary>
/// 系统环境检测：Windows 版本、架构、CPU、内存、磁盘、管理员权限、PowerShell、winget。
/// </summary>
public static class SystemInfoProvider
{
    public static List<SystemInfoItem> Collect()
    {
        var items = new List<SystemInfoItem>
        {
            new() { Key = "操作系统", Value = GetWindowsVersion() },
            new() { Key = "系统架构", Value = RuntimeInformation.OSArchitecture.ToString() },
            new() { Key = "CPU", Value = $"{Environment.ProcessorCount} 核" },
            new() { Key = "内存", Value = GetTotalMemory() },
            new() { Key = "管理员权限", Value = IsAdministrator() ? "是" : "否" },
            new() { Key = "PowerShell", Value = GetPowerShellVersion() },
            new() { Key = "winget", Value = WingetHelper.GetVersion() ?? "未安装" }
        };

        // 磁盘空间
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                items.Add(new SystemInfoItem
                {
                    Key = $"磁盘 {drive.Name}",
                    Value = $"剩余 {FormatBytes(drive.AvailableFreeSpace)} / 共 {FormatBytes(drive.TotalSize)}"
                });
            }
        }
        catch { /* 部分盘不可读时忽略 */ }

        return items;
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetWindowsVersion()
    {
        try
        {
            var (major, minor, build) = RtlGetVersion();
            var name = build >= 22000 ? "Windows 11" : (build >= 10240 ? "Windows 10" : $"Windows {major}.{minor}");
            return $"{name} (Build {build})";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static string GetTotalMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref status))
                return FormatBytes((long)status.ullTotalPhys);
        }
        catch { }
        return "-";
    }

    private static string GetPowerShellVersion()
    {
        var r = CommandRunner.Run("powershell.exe", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", timeoutMs: 15_000);
        return r.Succeeded ? r.Output.Trim() : "-";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {units[i]}";
    }

    // ---- P/Invoke ----
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OSVERSIONINFOEX lpVersionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OSVERSIONINFOEX
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
    }

    private static (int Major, int Minor, int Build) RtlGetVersion()
    {
        var info = new OSVERSIONINFOEX { dwOSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEX>() };
        if (RtlGetVersion(ref info) == 0)
            return (info.dwMajorVersion, info.dwMinorVersion, info.dwBuildNumber);
        return (0, 0, 0);
    }
}
