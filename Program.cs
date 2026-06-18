using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

// ===== 配置常量 =====
const string LogFile = "IdleShutdown.log";
const string EventLogSource = "IdleShutdown";

// 默认参数值
const int DefaultCheckHour = 19;
const int DefaultIdleThresholdMinutes = 60;
const int DefaultBootTimeToleranceMinutes = 5;
const int DefaultCheckIntervalSeconds = 5;
const bool DefaultRequireBootTimeTolerance = true;

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

        case "--config":
        case "-c":
            ConfigureSettings(cmdArgs.Skip(1).ToArray());
            return;

        case "--show-config":
        case "-sc":
            ShowConfiguration();
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

// ===== 初始化事件日志源 =====
InitializeEventLog();

// ===== 读取配置参数 =====
var checkHour = ReadRegInt("CheckHour", DefaultCheckHour);
var idleThresholdMinutes = ReadRegInt("IdleThresholdMinutes", DefaultIdleThresholdMinutes);
var bootTimeToleranceMinutes = ReadRegInt("BootTimeToleranceMinutes", DefaultBootTimeToleranceMinutes);
var checkIntervalSeconds = ReadRegInt("CheckIntervalSeconds", DefaultCheckIntervalSeconds);
var requireBootTimeTolerance = ReadRegBool("RequireBootTimeTolerance", DefaultRequireBootTimeTolerance);

Log($"程序启动于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Log($"配置: 检查时刻={checkHour}点, 空闲阈值={idleThresholdMinutes}分钟, 开机误差={bootTimeToleranceMinutes}分钟, 检查间隔={checkIntervalSeconds}秒, 检查开机时间={requireBootTimeTolerance}");

// ===== 检查开机时刻 =====
if (requireBootTimeTolerance)
{
    var bootTime = GetSystemBootTime();
    var uptime = DateTime.Now - bootTime;

    Log($"系统开机时间: {bootTime:yyyy-MM-dd HH:mm:ss}, 已运行时间: {uptime.TotalMinutes:F2} 分钟");

    if (uptime.TotalMinutes > bootTimeToleranceMinutes)
    {
        Log($"系统开机已超过 {bootTimeToleranceMinutes} 分钟，无需检查，退出。");
        return;
    }

    Log($"系统开机在 {bootTimeToleranceMinutes} 分钟内，开始监控系统空闲状态...");
}
else
{
    Log("不检查开机时间，直接开始监控系统空闲状态...");
}
var idleThresholdMs = idleThresholdMinutes * 60 * 1000;
var checkIntervalMs = checkIntervalSeconds * 1000;
int consecutiveIdleChecks = 0;
const int requiredConsecutiveChecks = 12; // 连续 60 秒确认空闲，避免误判

while (true)
{
    var idleTimeMs = GetIdleTimeMs();
    if (idleTimeMs >= idleThresholdMs)
    {
        consecutiveIdleChecks++;
        Log($"空闲检测: 已空闲 {idleTimeMs / 1000} 秒 ({consecutiveIdleChecks}/{requiredConsecutiveChecks} 次确认)");

        if (consecutiveIdleChecks >= requiredConsecutiveChecks)
        {
            Log("系统已空闲超过配置阈值，准备关机...");
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

    Thread.Sleep(checkIntervalMs);
}

// ===== 命令行管理功能 =====

static void PrintHelp()
{
    Console.WriteLine("IdleShutdown - 系统空闲检测与自动关机程序");
    Console.WriteLine();
    Console.WriteLine("用法:");
    Console.WriteLine("  IdleShutdown                     运行空闲监控（无参数默认模式）");
    Console.WriteLine("  IdleShutdown --install / -i       安装计划任务（需管理员权限）");
    Console.WriteLine("  IdleShutdown --uninstall / -u     卸载计划任务（需管理员权限）");
    Console.WriteLine("  IdleShutdown --config / -c <参数>  配置参数（需管理员权限）");
    Console.WriteLine("  IdleShutdown --show-config / -sc  显示当前配置");
    Console.WriteLine("  IdleShutdown --help / -h / /?     显示此帮助");
    Console.WriteLine();
    Console.WriteLine("配置参数（--config 后使用）:");
    Console.WriteLine("  CheckHour <小时>                  检查时刻（0-23，默认19）");
    Console.WriteLine("  IdleThreshold <分钟>             空闲阈值（默认60分钟）");
    Console.WriteLine("  BootTolerance <分钟>             开机误差（默认5分钟）");
    Console.WriteLine("  CheckInterval <秒>               检查间隔（默认5秒）");
    Console.WriteLine("  RequireBootTolerance <true|false> 是否检查开机时间（默认true）");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  IdleShutdown --config CheckHour 20 IdleThreshold 30");
    Console.WriteLine("  IdleShutdown --config BootTolerance 10 RequireBootTolerance false");
    Console.WriteLine();
    Console.WriteLine("说明:");
    Console.WriteLine("  程序启动时检查系统开机时间。若开机在指定误差范围内，");
    Console.WriteLine("  则开始监控空闲时间，若达到阈值则自动关机。");
    Console.WriteLine("  配置保存在: HKEY_LOCAL_MACHINE\\SOFTWARE\\IdleShutdown");
}

static void ConfigureSettings(string[] args)
{
    if (!IsAdministrator())
    {
        Console.WriteLine("[错误] 配置参数需要管理员权限，请以管理员身份运行。");
        return;
    }

    if (args.Length < 2 || args.Length % 2 != 0)
    {
        Console.WriteLine("[错误] 参数格式错误。使用: --config <参数名> <值> [<参数名> <值>]...");
        PrintHelp();
        return;
    }

    try
    {
        for (int i = 0; i < args.Length; i += 2)
        {
            var paramName = args[i];
            var paramValue = args[i + 1];

            switch (paramName.ToLowerInvariant())
            {
                case "checkhour":
                    if (int.TryParse(paramValue, out var hour) && hour >= 0 && hour <= 23)
                    {
                        WriteRegValue("CheckHour", hour);
                        Console.WriteLine($"[成功] CheckHour 已设置为 {hour}");
                    }
                    else
                    {
                        Console.WriteLine($"[错误] CheckHour 必须是 0-23 之间的整数");
                    }
                    break;

                case "idlethreshold":
                    if (int.TryParse(paramValue, out var idle) && idle > 0)
                    {
                        WriteRegValue("IdleThresholdMinutes", idle);
                        Console.WriteLine($"[成功] IdleThresholdMinutes 已设置为 {idle} 分钟");
                    }
                    else
                    {
                        Console.WriteLine($"[错误] IdleThreshold 必须是正整数");
                    }
                    break;

                case "boottolerance":
                    if (int.TryParse(paramValue, out var boot) && boot > 0)
                    {
                        WriteRegValue("BootTimeToleranceMinutes", boot);
                        Console.WriteLine($"[成功] BootTimeToleranceMinutes 已设置为 {boot} 分钟");
                    }
                    else
                    {
                        Console.WriteLine($"[错误] BootTolerance 必须是正整数");
                    }
                    break;

                case "checkinterval":
                    if (int.TryParse(paramValue, out var interval) && interval > 0)
                    {
                        WriteRegValue("CheckIntervalSeconds", interval);
                        Console.WriteLine($"[成功] CheckIntervalSeconds 已设置为 {interval} 秒");
                    }
                    else
                    {
                        Console.WriteLine($"[错误] CheckInterval 必须是正整数");
                    }
                    break;

                case "requireboottolerance":
                    if (bool.TryParse(paramValue, out var requireBoot))
                    {
                        WriteRegValue("RequireBootTimeTolerance", requireBoot ? 1 : 0);
                        Console.WriteLine($"[成功] RequireBootTimeTolerance 已设置为 {requireBoot}");
                    }
                    else
                    {
                        Console.WriteLine($"[错误] RequireBootTolerance 必须是 true 或 false");
                    }
                    break;

                default:
                    Console.WriteLine($"[警告] 未知参数: {paramName}");
                    break;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 配置失败: {ex.Message}");
    }
}

static void ShowConfiguration()
{
    Console.WriteLine("当前配置：");
    Console.WriteLine($"  CheckHour (检查时刻):               {ReadRegInt("CheckHour", DefaultCheckHour)} 点");
    Console.WriteLine($"  IdleThresholdMinutes (空闲阈值):    {ReadRegInt("IdleThresholdMinutes", DefaultIdleThresholdMinutes)} 分钟");
    Console.WriteLine($"  BootTimeToleranceMinutes (开机误差): {ReadRegInt("BootTimeToleranceMinutes", DefaultBootTimeToleranceMinutes)} 分钟");
    Console.WriteLine($"  CheckIntervalSeconds (检查间隔):    {ReadRegInt("CheckIntervalSeconds", DefaultCheckIntervalSeconds)} 秒");
    Console.WriteLine($"  RequireBootTimeTolerance (检查开机): {ReadRegBool("RequireBootTimeTolerance", DefaultRequireBootTimeTolerance)}");
    Console.WriteLine();
    Console.WriteLine("配置位置: HKEY_LOCAL_MACHINE\\SOFTWARE\\IdleShutdown");
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
/// 初始化事件日志源
/// </summary>
static void InitializeEventLog()
{
    try
    {
        if (!EventLog.SourceExists(EventLogSource))
        {
            EventLog.CreateEventSource(EventLogSource, "Application");
        }
    }
    catch
    {
        // 如果没有管理员权限，初始化失败也不影响主流程
    }
}

/// <summary>
/// 获取系统开机时间
/// </summary>
static DateTime GetSystemBootTime()
{
    try
    {
        // 通过 uptime 计算开机时间
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return DateTime.Now - uptime;
    }
    catch
    {
        return DateTime.Now; // 获取失败默认为现在时间
    }
}

/// <summary>
/// 从注册表读取整数值
/// </summary>
static int ReadRegInt(string valueName, int defaultValue)
{
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\IdleShutdown");
        if (key?.GetValue(valueName) is int value)
        {
            return value;
        }
    }
    catch
    {
        // 注册表读取失败使用默认值
    }
    return defaultValue;
}

/// <summary>
/// 从注册表读取布尔值
/// </summary>
static bool ReadRegBool(string valueName, bool defaultValue)
{
    try
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\IdleShutdown");
        if (key?.GetValue(valueName) is int value)
        {
            return value != 0;
        }
    }
    catch
    {
        // 注册表读取失败使用默认值
    }
    return defaultValue;
}

/// <summary>
/// 写入注册表值
/// </summary>
static void WriteRegValue(string valueName, object value)
{
    try
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\IdleShutdown");
        key.SetValue(valueName, value);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 写入注册表失败: {ex.Message}");
    }
}
static void Shutdown()
{
    Log("正在关闭系统...");
    
    try
    {
        // 使用 shutdown 命令关闭系统，1 分钟后关机
        var psi = new ProcessStartInfo("shutdown")
        {
            Arguments = "/s /t 60 /c \"IdleShutdown: 系统空闲超过配置阈值，自动关机。\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            Log("关机命令已发送，系统将在 1 分钟后关闭。");
        }
        else
        {
            Log($"关机命令失败 (错误码: {proc.ExitCode}): {error}");
        }
    }
    catch (Exception ex)
    {
        Log($"关机异常: {ex.Message}");
    }
}

static void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(line);
    
    // 写入日志文件
    try
    {
        File.AppendAllText(LogFile, line + Environment.NewLine);
    }
    catch
    {
        // 日志写入失败不影响主流程
    }
    
    // 写入 Windows 事件日志
    try
    {
        using var eventLog = new EventLog("Application")
        {
            Source = EventLogSource
        };
        eventLog.WriteEntry(message, EventLogEntryType.Information);
    }
    catch
    {
        // 事件日志写入失败不影响主流程
    }
}

// ===== 内部类：包含 P/Invoke 定义 =====

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
}
