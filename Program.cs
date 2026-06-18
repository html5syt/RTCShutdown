using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;

// ===== 命令行参数处理 =====
var cmdArgs = args; // args 由编译器自动提供

if (cmdArgs.Length > 0)
{
    switch (cmdArgs[0].ToLowerInvariant())
    {
        case "--install":
        case "-i":
            InstallScheduledTask();
            return;

        case "--uninstall":
        case "-u":
            UninstallScheduledTask();
            return;

        case "--help":
        case "-h":
        case "/?":
            PrintHelp();
            return;

        default:
            Console.WriteLine($"未知参数: {cmdArgs[0]}");
            PrintHelp();
            return;
    }
}

// ===== 无参数：正常执行空闲监控 =====

const string LogFile = "IdleShutdown.log";

Log($"程序启动于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

// 检查当前时间是否在 19:00 附近（18:30~19:30）
var now = DateTime.Now;
var targetStart = now.Date.AddHours(18).AddMinutes(30);
var targetEnd = now.Date.AddHours(19).AddMinutes(30);

if (now < targetStart || now > targetEnd)
{
    Log($"当前时间 {now:HH:mm:ss} 不在 18:30~19:30 范围内，无需监控，退出。");
    return;
}

Log($"当前时间 {now:HH:mm:ss} 在监控窗口内，开始监控系统空闲状态...");

// 持续检测空闲时间
const int IdleThresholdMs = 3_600_000; // 1 小时（毫秒）
const int CheckIntervalMs = 5_000;      // 每 5 秒检测一次
int consecutiveIdleChecks = 0;
const int requiredConsecutiveChecks = 12; // 连续 60 秒确认空闲，避免误判

while (true)
{
    var idleTimeMs = GetIdleTimeMs();
    if (idleTimeMs >= IdleThresholdMs)
    {
        consecutiveIdleChecks++;
        Log($"空闲检测: 已空闲 {idleTimeMs / 1000} 秒 ({consecutiveIdleChecks}/{requiredConsecutiveChecks} 次确认)");

        if (consecutiveIdleChecks >= requiredConsecutiveChecks)
        {
            Log("系统已空闲超过 1 小时，准备关机...");
            Shutdown();
            return;
        }
    }
    else
    {
        if (consecutiveIdleChecks > 0)
        {
            Log($"检测到用户活动，重置空闲计数器（当前空闲 {idleTimeMs / 1000} 秒）");
            consecutiveIdleChecks = 0;
        }
    }

    Thread.Sleep(CheckIntervalMs);
}

// ===== 命令行管理功能 =====

static void PrintHelp()
{
    Console.WriteLine("IdleShutdown - RTC 唤醒后空闲关机程序");
    Console.WriteLine();
    Console.WriteLine("用法:");
    Console.WriteLine("  IdleShutdown                     运行空闲监控（无参数默认模式）");
    Console.WriteLine("  IdleShutdown --install / -i       安装计划任务（需管理员权限）");
    Console.WriteLine("  IdleShutdown --uninstall / -u     卸载计划任务（需管理员权限）");
    Console.WriteLine("  IdleShutdown --help / -h / /?     显示此帮助");
    Console.WriteLine();
    Console.WriteLine("说明:");
    Console.WriteLine("  程序由计划任务在每天 19:00 RTC 唤醒启动后运行，");
    Console.WriteLine("  若 1 小时内无鼠标/键盘操作则自动关机。");
}

static void InstallScheduledTask()
{
    if (!IsAdministrator())
    {
        Console.WriteLine("[错误] 安装计划任务需要管理员权限，请以管理员身份运行。");
        return;
    }

    var exePath = Environment.ProcessPath!;
    var taskName = "IdleShutdown";

    Console.WriteLine("正在创建计划任务...");

    try
    {
        // 使用 schtasks 创建任务（兼容性好）
        var psi = new ProcessStartInfo("schtasks")
        {
            Arguments = $@"/Create /F /TN ""{taskName}"" /TR ""'{exePath}'"" /SC ONSTART /DELAY 0000:30 /RL HIGHEST /RU SYSTEM /IT",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            Console.WriteLine("[成功] 计划任务已创建！");
            Console.WriteLine($"  任务名称: {taskName}");
            Console.WriteLine("  触发器:   系统启动时（延迟30秒）");
            Console.WriteLine("  运行账户: SYSTEM");
            Console.WriteLine("  权限:     最高权限");
            Console.WriteLine();
            Console.WriteLine("程序将在每天 19:00 附近 RTC 唤醒后自动运行，");
            Console.WriteLine("若 1 小时内无任何操作则自动关机。");
        }
        else
        {
            Console.WriteLine($"[错误] 创建任务失败: {error}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 创建任务异常: {ex.Message}");
    }
}

static void UninstallScheduledTask()
{
    if (!IsAdministrator())
    {
        Console.WriteLine("[错误] 卸载计划任务需要管理员权限，请以管理员身份运行。");
        return;
    }

    var taskName = "IdleShutdown";
    Console.WriteLine("正在删除计划任务...");

    try
    {
        var psi = new ProcessStartInfo("schtasks")
        {
            Arguments = $@"/Delete /F /TN ""{taskName}""",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            Console.WriteLine("[成功] 计划任务已删除。");
        }
        else
        {
            Console.WriteLine($"[错误] 删除失败: {error}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 删除任务异常: {ex.Message}");
    }
}

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

// ===== 工具方法 =====

/// <summary>
/// 获取自上次用户输入（鼠标/键盘）以来的空闲时间（毫秒）
/// </summary>
static uint GetIdleTimeMs()
{
    var lastInputInfo = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
    if (NativeMethods.GetLastInputInfo(ref lastInputInfo))
    {
        var tickCount = (uint)Environment.TickCount;
        var tickDelta = tickCount - lastInputInfo.dwTime;
        return tickDelta;
    }
    return 0;
}

/// <summary>
/// 执行系统关机
/// </summary>
static void Shutdown()
{
    // 启用关机权限
    var token = IntPtr.Zero;
    var tokenPrivileges = new TOKEN_PRIVILEGES
    {
        PrivilegeCount = 1,
        Luid = new LUID(),
        Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
    };

    if (NativeMethods.OpenProcessToken(Process.GetCurrentProcess().Handle,
            NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out token))
    {
        NativeMethods.LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid);
        tokenPrivileges.Luid = luid;

        NativeMethods.AdjustTokenPrivileges(token, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.CloseHandle(token);
    }

    Log("正在关闭系统...");

    // 尝试 InitiateShutdown（优先）
    var result = NativeMethods.InitiateShutdown(
        null,                     // 本地计算机
        "系统空闲超过1小时，自动关机。",
        0,                        // 无超时
        NativeMethods.SHUTDOWN_SHUTDOWN | NativeMethods.SHUTDOWN_POWEROFF | NativeMethods.SHUTDOWN_INSTALL_UPDATES | NativeMethods.SHUTDOWN_RESTART,
        NativeMethods.SHTDN_REASON_MAJOR_OPERATINGSYSTEM | NativeMethods.SHTDN_REASON_MINOR_OTHER | NativeMethods.SHTDN_REASON_FLAG_PLANNED);

    if (result == 0)
    {
        // 如果 InitiateShutdown 失败，回退到 ExitWindowsEx
        Log($"InitiateShutdown 失败 (错误码: {Marshal.GetLastWin32Error()})，尝试 ExitWindowsEx...");
        NativeMethods.ExitWindowsEx(NativeMethods.EWX_SHUTDOWN | NativeMethods.EWX_POWEROFF,
            NativeMethods.SHTDN_REASON_MAJOR_OPERATINGSYSTEM | NativeMethods.SHTDN_REASON_MINOR_OTHER | NativeMethods.SHTDN_REASON_FLAG_PLANNED);
    }
}

static void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(line);
    try
    {
        File.AppendAllText(LogFile, line + Environment.NewLine);
    }
    catch
    {
        // 日志写入失败不影响主流程
    }
}

// ===== 内部类：包含 P/Invoke 定义和常量 =====

[StructLayout(LayoutKind.Sequential)]
struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

static partial class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int InitiateShutdown(string? lpMachineName, string lpMessage,
        int dwGracePeriod, uint dwShutdownFlags, uint dwReason);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    // 权限常量
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    // 关机标志
    public const uint SHUTDOWN_SHUTDOWN = 0x00000001;      // 正常关机
    public const uint SHUTDOWN_POWEROFF = 0x00000008;       // 关闭电源
    public const uint SHUTDOWN_RESTART = 0x00000004;        // 重启
    public const uint SHUTDOWN_INSTALL_UPDATES = 0x00000040; // 安装更新

    // 关机原因
    public const uint SHTDN_REASON_MAJOR_OPERATINGSYSTEM = 0x00020000;
    public const uint SHTDN_REASON_MINOR_OTHER = 0x00000001;
    public const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    // ExitWindowsEx 标志
    public const uint EWX_SHUTDOWN = 0x00000001;
    public const uint EWX_POWEROFF = 0x00000008;
}

[StructLayout(LayoutKind.Sequential)]
struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
}
