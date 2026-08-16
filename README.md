# Z690 Monitor

一个面向 Windows 11、Intel Core i9-13900K 和 Z690 主板的小型硬件监控程序。它每秒读取一次传感器，并显示：

- CPU 各核心当前温度的平均值和最高值
- CPU 各核心当前 VID 的平均值和最高值
- PCH（芯片组）温度

程序提供三种显示方式：

- 任务栏通知区域图标：图标数字是 CPU 最高核心温度，悬停显示全部读数
- 任务栏上方监控条：40px 高的常驻紧凑窗口，一次显示全部五项读数
- 完整面板：选择 PCH 传感器、设置开机启动并导出传感器报告

> Windows 11 已移除旧式 DeskBand/任务栏工具栏。普通应用没有受支持的接口可把任意实时文本嵌进 Explorer 任务栏。本项目使用通知区域图标和贴靠任务栏上方的独立窗口，避免注入 Explorer。

## 构建

需要 Windows 11 x64 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。在仓库目录执行：

```powershell
dotnet restore .\Win11Monitor.sln
dotnet build .\Win11Monitor.sln -c Release
dotnet test .\Win11Monitor.sln -c Release --no-build
```

生成可直接分发的自包含版本：

```powershell
.\build.ps1 -Publish
```

输出位于 `artifacts\Z690Monitor`。运行 `Z690Monitor.exe` 时会请求管理员权限，这是读取 CPU MSR、PCI 和主板 Super-I/O 传感器所必需的。程序不修改 BIOS 参数，也不控制风扇或电压。

## 使用

1. 启动 `Z690Monitor.exe` 并接受 UAC 请求。
2. 如果 PCH 显示为 `--`，打开完整面板，在“PCH 温度传感器”中选择主板对应项。
3. 在 Windows 设置的“个性化 > 任务栏 > 其他系统托盘图标”中固定 `Z690 Monitor`，防止图标进入折叠菜单。
4. 可选中“登录 Windows 后自动启动”。程序会创建一个仅对当前用户生效、最高权限运行的登录计划任务，从而避免每次登录再次弹出 UAC。

使用自动隐藏任务栏或前台运行全屏程序时，紧凑监控条会自动收起，托盘读数仍会继续更新。

不同主板厂商对 Z690 PCH 传感器的命名和开放程度不同。自动模式只匹配 `PCH`、`Chipset` 或 `Platform Controller Hub`，不会把未知的 `Temperature #n` 猜成 PCH。若无法确定，使用“导出传感器报告”核对来源。

## 数据含义

- CPU 温度为本次采样中各核心温度的算术平均值和最大值，不是启动以来的历史统计。
- CPU VID 是处理器向供电系统请求的电压，并不等于主板 VRM 实测 Vcore。界面会明确显示实际采用的数据源。
- 无效、缺失、`NaN` 或无穷读数显示为 `--`，不会当作 0 参与计算。

如果 Windows 内存完整性、易受攻击驱动程序阻止列表或安全软件阻止 LibreHardwareMonitor 驱动，界面会显示读取失败。不要为了运行本程序关闭 Windows 安全功能；应先升级依赖或使用已被当前安全策略接受的驱动版本。Armoury Crate、HWiNFO、FanControl 等工具同时轮询 EC/SMBus 时也可能产生冲突。

设置保存在 `%LOCALAPPDATA%\Z690Monitor\settings.json`。

## 许可证

本项目使用 Apache-2.0 许可证。硬件读取依赖 MPL-2.0 许可的 LibreHardwareMonitor，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
