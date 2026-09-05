using System.Diagnostics;
using System.Text;

namespace DevKit.Core;

/// <summary>
/// 命令执行器：运行外部命令（winget、python 等），支持超时、输出回调和取消。
/// 所有执行的命令都会写入日志（安全要求：安装命令写入日志）。
/// </summary>
public static class CommandRunner
{
    /// <summary>命令执行结果</summary>
    public class Result
    {
        public int ExitCode { get; set; } = -1;
        public string Output { get; set; } = "";
        public bool TimedOut { get; set; }
        public bool Canceled { get; set; }
        public bool Succeeded => ExitCode == 0;
    }

    /// <summary>
    /// 执行命令并等待完成。
    /// </summary>
    /// <param name="fileName">可执行文件</param>
    /// <param name="arguments">参数</param>
    /// <param name="timeoutMs">超时（毫秒），0 表示不超时</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="outputCallback">实时输出回调（合并 stdout/stderr）</param>
    /// <param name="cancelToken">取消令牌</param>
    public static Result Run(
        string fileName,
        string arguments = "",
        int timeoutMs = 120_000,
        string? workingDirectory = null,
        Action<string>? outputCallback = null,
        CancellationToken? cancelToken = null)
    {
        Logger.Info($"执行命令: {fileName} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        using var outputWait = new ManualResetEvent(false);
        using var errorWait = new ManualResetEvent(false);

        // 用字符流读取，按 \r / \n 分割行（winget 下载进度用 \r 覆盖同一行）
        async Task ReadStreamAsync(System.IO.StreamReader reader, Action<string>? cb, ManualResetEvent wh)
        {
            var buffer = new char[2048];
            var line = new StringBuilder();
            while (true)
            {
                int n = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (n == 0) break;
                for (int i = 0; i < n; i++)
                {
                    char c = buffer[i];
                    if (c == '\r' || c == '\n')
                    {
                        if (line.Length > 0)
                        {
                            string text = line.ToString();
                            line.Clear();
                            // \r\n 跳过紧跟的 \n
                            if (c == '\r' && i + 1 < n && buffer[i + 1] == '\n') i++;
                            lock (output) output.AppendLine(text);
                            cb?.Invoke(text);
                        }
                    }
                    else
                    {
                        line.Append(c);
                    }
                }
            }
            if (line.Length > 0)
            {
                string text = line.ToString();
                lock (output) output.AppendLine(text);
                cb?.Invoke(text);
            }
            wh.Set();
        }

        var result = new Result();
        try
        {
            if (!process.Start())
            {
                result.Output = $"无法启动进程: {fileName}";
                Logger.Error(result.Output);
                return result;
            }

            var outReader = new System.IO.StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);
            var errReader = new System.IO.StreamReader(process.StandardError.BaseStream, Encoding.UTF8);
            _ = ReadStreamAsync(outReader, outputCallback, outputWait);
            _ = ReadStreamAsync(errReader, outputCallback, errorWait);

            // 等待退出（支持取消与超时）
            var waitToken = cancelToken ?? CancellationToken.None;
            while (!process.HasExited)
            {
                if (waitToken.IsCancellationRequested)
                {
                    result.Canceled = true;
                    try { process.Kill(true); } catch { }
                    break;
                }
                if (timeoutMs > 0 && !process.WaitForExit(200))
                {
                    if ((DateTime.Now - process.StartTime).TotalMilliseconds > timeoutMs)
                    {
                        result.TimedOut = true;
                        try { process.Kill(true); } catch { }
                        break;
                    }
                }
            }

            // 等待输出流排空
            if (!result.Canceled && !result.TimedOut)
            {
                process.WaitForExit();
                outputWait.WaitOne(3000);
                errorWait.WaitOne(3000);
            }

            result.ExitCode = process.HasExited ? process.ExitCode : -1;
            result.Output = output.ToString().Trim();
            if (result.Canceled) result.Output += Environment.NewLine + "(已取消)";
            if (result.TimedOut) result.Output += Environment.NewLine + $"(执行超时 {timeoutMs}ms)";
            Logger.Info($"命令结束: exit={result.ExitCode} canceled={result.Canceled} timeout={result.TimedOut}");
            return result;
        }
        catch (Exception ex)
        {
            result.Output = $"命令执行异常: {ex.Message}";
            Logger.Error(result.Output);
            return result;
        }
    }
}
