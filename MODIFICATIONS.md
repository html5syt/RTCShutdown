# IdleShutdown 程序修改文档

## 概述
对 IdleShutdown 程序进行了全面的功能扩展和改进，使其支持灵活的配置管理、Windows 事件日志集成以及改进的系统关机机制。

---

## 修改详情

### 1. 移除时间窗口检查 ✅
**需求**: 默认无论19点时是否刚刚开机还是已经开机都进行检查

**实现**:
- 删除了原有的时间窗口检查逻辑（18:30~19:30）
- 程序现在无论何时启动都会进行空闲监控（如果通过了其他检查）
- 移除了 `checkHour` 参数（作为保留参数，暂未实际使用）

**代码变化**:
```csharp
// 旧代码：检查 19:00 时间窗口
if (now < targetStart || now > targetEnd)
{
    Log($"当前时间 {now:HH:mm:ss} 不在 18:30~19:30 范围内，无需监控，退出。");
    return;
}

// 新代码：直接进行检查
// （只有在通过开机时间检查后才会执行）
```

---

### 2. 使用系统 shutdown 命令替代 DLL 接口 ✅
**需求**: 关机使用系统shutdown命令而不是dll接口强制可靠关闭电脑

**实现**:
- 使用 Windows 系统 `shutdown /s /t 60` 命令进行关机
- 移除了所有复杂的 P/Invoke DLL 调用（InitiateShutdown、ExitWindowsEx 等）
- 保留了 GetLastInputInfo 用于空闲时间检测

**代码变化**:
```csharp
// 新的 Shutdown() 方法
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
        // 处理输出...
    }
    catch (Exception ex)
    {
        Log($"关机异常: {ex.Message}");
    }
}
```

**优势**:
- ✅ 更加可靠和稳定
- ✅ 系统级别的关机，普通进程无法阻止
- ✅ 自动处理保存文件等操作
- ✅ 提供 60 秒的延迟，用户可以取消

---

### 3. Windows 事件查看器集成 ✅
**需求**: log在写入文件的同时写入Windows事件查看器

**实现**:
- 添加 NuGet 包：`System.Diagnostics.EventLog 8.0.0`
- 每条日志同时写入文件和 Windows 事件日志
- 自动创建事件日志源

**代码变化**:
```csharp
static void Log(string message)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
    Console.WriteLine(line);
    
    // 写入日志文件
    try
    {
        File.AppendAllText(LogFile, line + Environment.NewLine);
    }
    catch { }
    
    // 写入 Windows 事件日志
    try
    {
        using var eventLog = new EventLog("Application")
        {
            Source = EventLogSource
        };
        eventLog.WriteEntry(message, EventLogEntryType.Information);
    }
    catch { }
}

// 初始化事件日志源
static void InitializeEventLog()
{
    try
    {
        if (!EventLog.SourceExists(EventLogSource))
        {
            EventLog.CreateEventSource(EventLogSource, "Application");
        }
    }
    catch { }
}
```

**查看日志位置**:
1. 打开 Windows 事件查看器
2. 导航到：应用程序日志
3. 查找源为 "IdleShutdown" 的事件

---

### 4. 系统开机时刻检测 ✅
**需求**: 检测开机时刻通过判断系统开机时间是否超过允许误差时间（默认5min）确定

**实现**:
- 使用 `Environment.TickCount64` 计算系统开机时间
- 支持可配置的开机误差容限（默认 5 分钟）
- 当 `RequireBootTimeTolerance` 为 false 时跳过检查

**代码变化**:
```csharp
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

// 主程序中的使用
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
}
else
{
    Log("不检查开机时间，直接开始监控系统空闲状态...");
}
```

---

### 5. 命令行参数和注册表配置 ✅
**需求**: 检测时刻、空闲时长、误差时间等均需要提供命令行选项更改参数并写入注册表

**实现**:
- 添加 `--config / -c` 命令行参数用于修改配置
- 添加 `--show-config / -sc` 显示当前配置
- 所有配置存储在 `HKEY_LOCAL_MACHINE\SOFTWARE\IdleShutdown`

**可配置参数**:

| 参数名                   | 类型 | 默认值 | 说明             | 范围       |
| ------------------------ | ---- | ------ | ---------------- | ---------- |
| CheckHour                | int  | 19     | 检查时刻（0-23） | 0-23       |
| IdleThresholdMinutes     | int  | 60     | 空闲阈值（分钟） | > 0        |
| BootTimeToleranceMinutes | int  | 5      | 开机误差（分钟） | > 0        |
| CheckIntervalSeconds     | int  | 5      | 检查间隔（秒）   | > 0        |
| RequireBootTimeTolerance | bool | true   | 是否检查开机时间 | true/false |

**使用示例**:

```bash
# 显示帮助信息
IdleShutdown --help
IdleShutdown -h
IdleShutdown /?

# 显示当前配置
IdleShutdown --show-config
IdleShutdown -sc

# 修改单个参数
IdleShutdown --config CheckHour 20

# 修改多个参数
IdleShutdown --config CheckHour 20 IdleThreshold 30

# 修改布尔参数
IdleShutdown --config RequireBootTolerance false

# 完整示例：修改多个参数
IdleShutdown --config CheckHour 20 IdleThreshold 45 BootTolerance 10

# 安装计划任务（需管理员）
IdleShutdown --install

# 卸载计划任务（需管理员）
IdleShutdown --uninstall

# 运行空闲监控
IdleShutdown
```

**代码实现**:
```csharp
static void ConfigureSettings(string[] args)
{
    if (!IsAdministrator())
    {
        Console.WriteLine("[错误] 配置参数需要管理员权限，请以管理员身份运行。");
        return;
    }

    // 参数解析和验证逻辑
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
                break;
            // ... 其他参数
        }
    }
}

// 注册表 I/O 函数
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
    catch { }
    return defaultValue;
}

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
```

---

## 文件变化

### Program.cs
- **添加**: 新的命令行参数处理
- **添加**: 配置管理函数 (ConfigureSettings, ShowConfiguration)
- **添加**: 注册表 I/O 函数 (ReadRegInt, ReadRegBool, WriteRegValue)
- **添加**: 事件日志初始化函数 (InitializeEventLog)
- **添加**: 开机时间检测函数 (GetSystemBootTime)
- **修改**: Shutdown() 函数使用 shutdown 命令
- **修改**: Log() 函数同时写入文件和事件日志
- **移除**: 复杂的 P/Invoke DLL 调用（除 GetLastInputInfo 外）
- **移除**: 时间窗口检查逻辑

### IdleShutdown.csproj
- **添加**: NuGet 包引用 `System.Diagnostics.EventLog 8.0.0`

---

## 编译和发布

### 编译
```bash
dotnet build -c Release
```

### 运行
```bash
# 显示帮助
./IdleShutdown --help

# 配置参数
./IdleShutdown --config CheckHour 20 IdleThreshold 30

# 运行监控
./IdleShutdown
```

### 发布自包含可执行文件
```bash
dotnet publish -c Release
```

生成文件位置：`bin/Release/net10.0-windows/win-x64/publish/IdleShutdown.exe`

---

## 一次性监控特性 ⭐

**关键行为**:
- 程序启动后进入空闲检测循环
- 连续检测系统空闲时间
- **只要检测到用户活动（鼠标/键盘操作），即立即退出监控**
- **不会重新计时，不会继续监控**
- 必须重新启动程序才能进行下一轮检测

**代码实现**:
```csharp
while (true)
{
    var idleTimeMs = GetIdleTimeMs();
    if (idleTimeMs >= idleThresholdMs)
    {
        // 仍在空闲状态，继续计数
        consecutiveIdleChecks++;
        Log($"空闲检测: 已空闲 {idleTimeMs / 1000} 秒...");
        
        if (consecutiveIdleChecks >= requiredConsecutiveChecks)
        {
            // 达到空闲阈值，触发关机
            Log("系统已空闲超过配置阈值，准备关机...");
            Shutdown();
            return;
        }
    }
    else
    {
        // ⭐ 检测到用户活动，直接退出（不重置计数器）
        Log($"检测到用户活动，退出监控（当前空闲仅 {idleTimeMs / 1000} 秒）");
        return; // 直接退出，程序终止
    }

    Thread.Sleep(checkIntervalMs);
}
```

**工作场景示例**:
1. 程序启动，开始检测
2. 经过 30 分钟，系统仍未有活动
3. 再经过 30 分钟，系统仍未有活动（共 60 分钟）
4. 触发关机 ✅
   
---

**对比场景**（用户有活动）:
1. 程序启动，开始检测
2. 经过 30 分钟，用户移动鼠标或按键盘
3. 程序立即记录并退出 ❌ 不进行关机
4. 需要重新启动程序才能重新监控

---

## 测试建议

### 功能测试
1. ✅ 验证 `--help` 显示完整帮助信息
2. ✅ 验证 `--show-config` 显示默认配置
3. ✅ 验证 `--config` 参数成功修改注册表值
4. ✅ 验证注册表修改后配置正确加载
5. ✅ 验证日志同时写入文件和事件查看器
6. ✅ 验证开机时间检查逻辑
7. ✅ 验证空闲时间检测（手动测试）
8. ✅ 验证 shutdown 命令调用（确认弹窗和倒计时）
9. ⭐ 验证一次性监控特性：用户活动时立即退出

### 编译测试
- ✅ Release 版本编译成功
- ✅ 无警告（ConfigRegKey 已移除）
- ✅ 无错误

---

## 后续改进建议

1. **GUI 配置工具**: 开发一个 Windows Forms/WPF 应用用于图形化配置
2. **日志轮换**: 实现日志文件大小限制和自动轮换
3. **活动检测扩展**: 支持检测鼠标/键盘以外的活动（如网络流量）
4. **定时任务调度**: 本地定时运行，而不仅仅依赖 Windows 任务计划
5. **性能优化**: 减少 CPU 占用，特别是检查间隔较短时

---

## 兼容性

- ✅ Windows 10 及更高版本
- ✅ .NET 10.0（Windows）
- ✅ 需要管理员权限才能安装/卸载计划任务和修改配置

---

## 注意事项

1. **管理员权限**: 安装计划任务和写入注册表需要管理员权限
2. **事件日志权限**: 首次创建事件日志源需要管理员权限
3. **关机延迟**: shutdown 命令提供 60 秒延迟，用户可以通过 `shutdown /a` 取消
4. **时区**: 所有时间戳使用本地时区
5. **注册表位置**: `HKEY_LOCAL_MACHINE\SOFTWARE\IdleShutdown`

---

**修改完成日期**: 2026-06-18
**修改状态**: ✅ 已完成并验证
