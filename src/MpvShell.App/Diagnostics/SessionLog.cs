using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MpvShell.App.Diagnostics;

internal static class SessionLog
{
    internal static SessionTraceListener? Start()
    {
        var defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MpvShell", "logs");
        var requestedDirectory = Environment.GetEnvironmentVariable("MPVSHELL_LOG_DIRECTORY");
        Exception? directoryFailure = null;
        SessionTraceListener? listener = null;
        foreach (var directory in new[] { requestedDirectory, defaultDirectory }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
        {
            try
            {
                var fullPath = Path.GetFullPath(directory!);
                Directory.CreateDirectory(fullPath);
                listener = new SessionTraceListener(Path.Combine(fullPath, $"session-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log"));
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                directoryFailure = ex;
            }
        }
        if (listener is null) return null;
        Trace.Listeners.Add(listener);
        Trace.AutoFlush = false;
        var assembly = typeof(SessionLog).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        Trace.WriteLine($"[app] MpvShell 启动；版本 {version}；构建 {configuration}；进程 {Environment.ProcessId}。");
        Trace.WriteLine($"[app] {RuntimeInformation.OSDescription}；{RuntimeInformation.FrameworkDescription}；进程架构 {RuntimeInformation.ProcessArchitecture}；CPU 逻辑核心 {Environment.ProcessorCount}。");
        Trace.WriteLine($"[app] 应用目录：{AppContext.BaseDirectory}；日志：{listener.FilePath}；请求的 libmpv 日志等级：{Environment.GetEnvironmentVariable("MPVSHELL_LOG_LEVEL") ?? "error"}。");
        if (directoryFailure is not null)
            Trace.WriteLine($"[logging] 指定的日志目录不可用，已回退默认目录：{directoryFailure.Message}");
        return listener;
    }
}
