# IdleShutdown 快速参考卡

## 常用命令

```bash
# 显示帮助
IdleShutdown --help

# 显示当前配置
IdleShutdown --show-config

# 修改配置（需管理员）
IdleShutdown --config <参数> <值> [<参数> <值>]...

# 安装计划任务（需管理员）
IdleShutdown --install

# 卸载计划任务（需管理员）
IdleShutdown --uninstall

# 运行监控
IdleShutdown
```

---

## 配置参数

| 参数                   | 默认值  | 范围       | 说明                       |
| ---------------------- | ------- | ---------- | -------------------------- |
| `CheckHour`            | 19      | 0-23       | 检查时刻（保留，暂未使用） |
| `IdleThreshold`        | 60 分钟 | > 0        | 空闲触发时间               |
| `BootTolerance`        | 5 分钟  | > 0        | 开机时间误差               |
| `CheckInterval`        | 5 秒    | > 0        | 检查间隔                   |
| `RequireBootTolerance` | true    | true/false | 是否检查开机时间           |

---

## 实用示例

### 修改单个参数
```bash
IdleShutdown --config IdleThreshold 30
```

### 修改多个参数
```bash
IdleShutdown --config IdleThreshold 45 BootTolerance 10 CheckInterval 10
```

### 禁用开机时间检查
```bash
IdleShutdown --config RequireBootTolerance false
```

### 恢复默认配置
```bash
IdleShutdown --config CheckHour 19 IdleThreshold 60 BootTolerance 5 CheckInterval 5 RequireBootTolerance true
```

---

## 日志查看

### 文件日志
位置: `IdleShutdown.log`（与程序同目录）

### 事件日志
1. 按 `Win + R`
2. 输入 `eventvwr.msc`
3. 导航到：应用程序日志
4. 查找源为 `IdleShutdown` 的事件

---

## 故障排查

| 问题           | 原因         | 解决方案                   |
| -------------- | ------------ | -------------------------- |
| 无法修改配置   | 无管理员权限 | 以管理员身份运行           |
| 找不到事件日志 | 事件源未创建 | 以管理员身份运行程序初始化 |
| 配置修改无效   | 程序已在运行 | 重启程序生效               |
| 关机失败       | 权限不足     | 检查 SYSTEM 账户权限       |

---

## 注册表位置

```
HKEY_LOCAL_MACHINE\SOFTWARE\IdleShutdown\
```

包含 5 个注册表项，对应上表中的 5 个参数。

---

## 工作流程

```
程序启动
  ├─ 解析命令行参数
  ├─ 初始化事件日志
  ├─ 从注册表读取配置
  ├─ 检查开机时间（可选）
  ├─ 进入空闲监控循环（⭐ 一次性）
  │   ├─ 每 5 秒检测一次空闲
  │   ├─ 若持续空闲超过阈值 → 执行关机
  │   └─ 若检测到用户活动 → 立即退出 ⭐
  └─ 若触发关机
      ├─ 调用 shutdown 命令
      └─ 60 秒后自动关机
```

**⭐ 一次性监控特性**：
- 程序启动后持续监控空闲状态
- 如果用户在达到空闲阈值前有任何操作 → 程序直接退出
- 不会重新计时，必须重新启动程序才能再次监控

---

## 计划任务配置

- **名称**: IdleShutdown
- **触发器**: 系统启动
- **延迟**: 30 秒
- **运行账户**: SYSTEM
- **权限**: 最高权限

---

## 版本信息

- **程序版本**: 2.0
- **.NET 版本**: .NET 10.0 (Windows)
- **最低系统**: Windows 10
- **需要权限**: 管理员（某些操作）

---

**快速参考 | IdleShutdown v2.0**
