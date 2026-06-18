# IdleShutdown — RTC 唤醒后空闲关机程序

一个 .NET 10 Windows 程序，用于在 **每天晚上 19:00 由 RTC 唤醒启动后**，检测 **1 小时内无任何鼠标/键盘操作** 则自动关闭电脑。

## 工作原理

1. 程序由 **Windows 计划任务** 在系统启动时自动运行
2. 判断当前时间是否在 **18:30~19:30** 范围内
   - **是** → 进入空闲监控模式
   - **否** → 立即退出，不做任何操作
3. 通过 Win32 API `GetLastInputInfo` 持续检测系统空闲时间
4. 若空闲时间达到 **1 小时**，调用 `InitiateShutdown` 执行关机
5. 监控日志写入 `IdleShutdown.log`（位于程序同目录）

## 系统要求

- Windows 10 / Windows 11（或 Windows Server 2019+）
- 需启用 BIOS/UEFI 中的 **RTC 唤醒**（定时开机）功能，并设置为每天 19:00 唤醒

## 快速开始

### 1. 发布单文件程序

```cmd
cd IdleShutdown
dotnet publish -c Release
```

输出路径：`bin\Release\net10.0-windows\win-x64\publish\IdleShutdown.exe`

### 2. 安装计划任务

以 **管理员身份** 运行程序：

```cmd
IdleShutdown --install
```

### 3. 验证任务

```powershell
Get-ScheduledTask -TaskName IdleShutdown | fl
```

或在 `taskschd.msc`（任务计划程序）中查看 `IdleShutdown` 任务。

### 4. 配置 RTC 唤醒（BIOS）

进入 BIOS 设置，找到 **RTC Alarm / Power On By RTC / Wake Up** 相关选项：

| 选项         | 值                   |
| ------------ | -------------------- |
| Wake Up Day  | Everyday / 0（每天） |
| Wake Up Time | 19:00                |
| Enable       | Enabled              |

不同主板品牌名称不同，请查阅主板手册。

## 卸载

以 **管理员身份** 运行：

```cmd
IdleShutdown --uninstall
```

或手动删除：

```cmd
schtasks /Delete /F /TN IdleShutdown
```

## 命令行参数

| 参数                   | 说明                         |
| ---------------------- | ---------------------------- |
| （无参数）             | 运行空闲监控（默认模式）     |
| `--install` / `-i`     | 安装计划任务（需管理员权限） |
| `--uninstall` / `-u`   | 卸载计划任务（需管理员权限） |
| `--help` / `-h` / `/?` | 显示帮助                     |

## 日志文件

程序运行时会生成 `IdleShutdown.log` 日志文件（与程序同目录），记录每次启动、检测和关机操作。

## 注意事项

- 计划任务以 **SYSTEM** 账户运行并启用最高权限，确保有关机权限
- 程序仅在 **18:30~19:30** 时间段内执行监控，其他时间启动会立即退出
- 若在 1 小时空闲期间突然有用户操作，空闲计数器会重置
- `IdleShutdown.log` 文件可帮助你排查程序是否正常启动

## 项目结构

```
IdleShutdown/
├── Program.cs                # 主程序代码
├── IdleShutdown.csproj       # 项目文件
├── app.manifest              # 管理员权限清单
└── README.md                 # 本文件
```
