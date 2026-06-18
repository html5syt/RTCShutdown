# IdleShutdown 使用指南

## 快速开始

### 1. 安装程序

#### 作为计划任务安装（推荐）
```batch
IdleShutdown --install
```
需要管理员权限运行。这会在系统启动时自动运行程序。

#### 直接运行
```batch
IdleShutdown
```

### 2. 配置参数

#### 查看当前配置
```batch
IdleShutdown --show-config
```

#### 修改配置
```batch
# 修改单个参数
IdleShutdown --config IdleThreshold 30

# 修改多个参数
IdleShutdown --config IdleThreshold 30 BootTolerance 10 CheckInterval 10

# 禁用开机时间检查
IdleShutdown --config RequireBootTolerance false

# 恢复默认配置
IdleShutdown --config CheckHour 19 IdleThreshold 60 BootTolerance 5 CheckInterval 5 RequireBootTolerance true
```

### 3. 卸载程序

```batch
IdleShutdown --uninstall
```
需要管理员权限运行。

---

## 配置参数详解

### CheckHour（检查时刻）
- **默认值**: 19
- **范围**: 0-23（24小时制）
- **说明**: 目前保留参数，暂未实际使用。计划后续功能中使用。

**示例**:
```batch
IdleShutdown --config CheckHour 20
```

### IdleThreshold（空闲阈值）
- **默认值**: 60（分钟）
- **范围**: > 0
- **说明**: 系统无鼠标/键盘操作超过此时间后触发关机

**示例** - 修改为30分钟:
```batch
IdleShutdown --config IdleThreshold 30
```

### BootTolerance（开机误差）
- **默认值**: 5（分钟）
- **范围**: > 0
- **说明**: 程序启动时，若系统开机时间在此范围内，则执行空闲检测

**示例** - 修改为10分钟:
```batch
IdleShutdown --config BootTolerance 10
```

### CheckInterval（检查间隔）
- **默认值**: 5（秒）
- **范围**: > 0
- **说明**: 每隔多久检测一次系统空闲时间

**示例** - 改为10秒检测一次:
```batch
IdleShutdown --config CheckInterval 10
```

### RequireBootTolerance（是否检查开机时间）
- **默认值**: true
- **值**: true / false
- **说明**: 
  - `true`: 程序启动时检查系统开机时间，只有在开机不超过BootTolerance时间时才执行
  - `false`: 跳过开机时间检查，直接开始空闲监控

**示例** - 禁用开机时间检查:
```batch
IdleShutdown --config RequireBootTolerance false
```

---

## 日志位置

### 文件日志
- **位置**: `IdleShutdown.log`（与程序在同一目录）
- **格式**: `[YYYY-MM-DD HH:MM:SS] 日志内容`

### Windows 事件日志
- **位置**: Windows 事件查看器 → 应用程序日志
- **事件源**: IdleShutdown
- **事件类型**: 信息事件

#### 查看事件日志的步骤
1. 打开 Windows 事件查看器（Win + R，输入 `eventvwr.msc`）
2. 在左侧导航栏找到"应用程序日志"
3. 在右侧事件列表中查找"IdleShutdown"源的事件
4. 双击打开查看详细信息

---

## 工作流程

### 程序启动流程
```
程序启动
    ↓
1. 解析命令行参数
    ├─ 如果是 --install → 安装计划任务 → 退出
    ├─ 如果是 --uninstall → 卸载计划任务 → 退出
    ├─ 如果是 --config → 修改配置 → 退出
    ├─ 如果是 --show-config → 显示配置 → 退出
    ├─ 如果是 --help → 显示帮助 → 退出
    └─ 否则继续正常流程
    ↓
2. 初始化事件日志源
    ↓
3. 从注册表读取配置
    ↓
4. 检查开机时间（如果 RequireBootTimeTolerance = true）
    ├─ 如果开机超过 BootTolerance 时间 → 记录日志 → 退出
    └─ 否则继续
    ↓
5. 进入空闲监控循环（一次性检测）
    ├─ 每隔 CheckInterval 秒检测一次空闲时间
    ├─ 如果空闲时间 ≥ IdleThreshold → 计数器+1
    ├─ 如果计数器达到 12 次 → 执行关机 → 正常退出
    └─ 若检测到用户活动（空闲时间 < IdleThreshold）
        → 记录日志 → 退出监控 ⭐ 一次性设计
    ↓
6. 若触发关机，调用 shutdown 命令
    ├─ 给用户 60 秒延迟
    ├─ 用户可通过 shutdown /a 取消
    └─ 60 秒后自动关机
```

**关键说明**:
- ⭐ **一次性设计**: 程序启动后，如果在达到空闲阈值前检测到任何用户活动（鼠标/键盘操作），会立即退出监控，不会重新计时。
- 只有当空闲时间连续达到配置阈值时（默认 60 分钟），才会执行关机。

### 计划任务配置
- **触发器**: 系统启动时
- **延迟**: 30 秒
- **运行账户**: SYSTEM
- **权限**: 最高权限

---

## 实际应用场景

### 场景1：办公电脑节能管理
**需求**: 电脑在无人使用时自动关机，节省电能

**配置**:
```batch
IdleShutdown --config IdleThreshold 60
IdleShutdown --config RequireBootTolerance false
IdleShutdown --install
```

- 任何时候启动都进行检测
- 1小时无操作后自动关机
- 开机后直接启动监控

### 场景2：定时开机关机周期
**需求**: 配合 RTC 定时启动，运行特定任务，然后关机

**配置**:
```batch
IdleShutdown --config IdleThreshold 5
IdleShutdown --config BootTolerance 10
IdleShutdown --config RequireBootTolerance true
IdleShutdown --install
```

- 开机5分钟内进行检测
- 允许系统开机误差为10分钟
- 若开机超过10分钟则退出（可能被手动启动）

### 场景3：频繁中断的工作环境
**需求**: 减少检测频率，避免误触发

**配置**:
```batch
IdleShutdown --config CheckInterval 30
IdleShutdown --config IdleThreshold 120
```

- 30秒检测一次（降低CPU占用）
- 2小时无操作才触发关机

---

## 故障排查

### 问题：程序无法启动任务
**症状**: `[错误] 安装计划任务需要管理员权限...`

**解决方案**:
1. 右键点击命令提示符
2. 选择"以管理员身份运行"
3. 重新执行安装命令

### 问题：无法写入配置
**症状**: `[错误] 写入注册表失败...`

**解决方案**:
1. 以管理员身份运行程序
2. 检查注册表权限
3. 查看具体错误信息

### 问题：日志文件无法写入
**症状**: 文件日志为空或缺失

**解决方案**:
1. 检查程序运行目录权限
2. 确保磁盘有足够空间
3. 运行时查看控制台是否有错误信息

### 问题：事件日志无法创建
**症状**: 事件查看器中找不到 IdleShutdown 源

**解决方案**:
1. 确保程序以管理员身份首次运行
2. 手动创建事件源（以管理员身份运行 PowerShell）:
```powershell
New-EventLog -LogName Application -Source IdleShutdown
```

---

## 高级用法

### 批量配置脚本
```batch
@echo off
REM 配置空闲关机参数
echo 配置 IdleShutdown...
IdleShutdown --config IdleThreshold 60
IdleShutdown --config BootTolerance 5
IdleShutdown --config CheckInterval 5
echo 安装计划任务...
IdleShutdown --install
echo 显示当前配置...
IdleShutdown --show-config
pause
```

### PowerShell 脚本
```powershell
# 检查程序状态
$taskName = "IdleShutdown"
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

if ($task) {
    Write-Host "任务已安装"
    Get-ScheduledTask -TaskName $taskName | Get-ScheduledTaskInfo
} else {
    Write-Host "任务未安装"
}
```

### 定期备份配置
```batch
REM 导出注册表配置
reg export "HKEY_LOCAL_MACHINE\SOFTWARE\IdleShutdown" IdleShutdown_backup.reg /y
REM 日期：%date% %time%
```

---

## 常见问题 (FAQ)

**Q: 程序可以同时监控多台电脑吗？**
A: 本程序是本地监控程序，需要在每台电脑上单独安装和配置。

**Q: 开机后一定会检测吗？**
A: 只有当开机时间在 BootTolerance 范围内，且 RequireBootTimeTolerance 为 true 时才会检测。

**Q: 程序会一直监控吗？**
A: ⭐ **不会**。这是一次性监控设计：
  - 程序启动后开始检测空闲时间
  - 如果在达到空闲阈值前用户有任何操作（鼠标/键盘），程序会立即退出
  - 不会重新计时，必须重新启动程序才能进行下一轮检测

**Q: 能否在关机前通知用户？**
A: shutdown 命令会显示倒计时窗口，用户可以通过 `shutdown /a` 取消关机。

**Q: 日志文件会无限增长吗？**
A: 当前版本会无限增长。建议定期清理或使用 Windows 日志轮换功能。

**Q: 能否在特定时间段禁用监控？**
A: 当前不支持。可通过临时卸载任务或修改 RequireBootTolerance 参数实现。

**Q: 配置修改后何时生效？**
A: 下次程序启动时生效。已在运行的程序需要重启才能应用新配置。

---

## 技术支持

- **日志位置**: 查看 `IdleShutdown.log` 文件
- **事件日志**: 打开 Windows 事件查看器查看应用程序日志
- **配置位置**: `HKEY_LOCAL_MACHINE\SOFTWARE\IdleShutdown`
- **命令帮助**: 运行 `IdleShutdown --help`

---

**最后更新**: 2026-06-18
**版本**: 2.0
**状态**: ✅ 完整功能版本
