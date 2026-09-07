# CPU 核心电压托盘监测器

面向 Windows 11 和 Intel Core i5-13600K 的小型托盘程序。程序每秒刷新一次，
先取当前所有有效 P-Core / E-Core VID 的最大值，再通过桌面左下角的任务栏按钮标题显示本次运行以来观测到的全局最大值。

## 功能

- 每 1 秒读取一次 CPU 电压。
- 左侧任务栏图标和右侧托盘图标都实时显示观测到的全局最大 VID；左侧标题同时显示 `最大 1.234 V`。
- 右侧托盘悬停按电压、频率、温度、功率顺序仅显示 `1.234V | 5.10GHz | 92°C | 181W`。
- EXE 和桌面快捷方式使用静态 `V` 图标；程序运行后的左右状态图标仍显示实际电压。
- 右键菜单显示四项峰值和有效核心数；左键单击右侧托盘图标打开详细状态窗口。
- 支持同时重置全部峰值、重新扫描传感器和退出。
- 支持当前用户登录后自动启动；计划任务以最高权限运行，不会在每次登录时弹 UAC。
- 发布结果是自包含的单个 EXE，不要求目标电脑预装 .NET。

> 这里显示的是处理器通过 `IA32_PERF_STATUS` 报告的逐核心 VID（请求电压），
> 不是主板 VRM 在插座处测得的 Vcore。现代 Intel 桌面 CPU 通常没有可供软件读取的
> “每核心实际电压”。
>
> “全局最大值”是本次程序运行期间实际观测到的最高 VID，可从右键菜单重置；
> 它不是 Intel 规格中的安全电压上限。

## 构建

需要 Windows 11 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
在 PowerShell 中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

`ExecutionPolicy Bypass` 只对当前构建进程生效，不会修改系统设置。

默认输出到 Windows 本机目录：

```text
%LOCALAPPDATA%\CpuCoreVoltageTray\
```

其中 `CpuCoreVoltageTray.exe` 是可直接运行的单文件程序；README、SETUP、第三方通知和完整许可证文本
随发布目录一起提供。

## 安装包

安装器构建、默认开机启动和卸载行为见 [SETUP.md](SETUP.md)。

## 使用

1. 在 PowerShell 运行 `& "$env:LOCALAPPDATA\CpuCoreVoltageTray\CpuCoreVoltageTray.exe"`。该目录路径稳定，适合开机自启动。
2. 首次运行会询问是否安装官方 PawnIO 2.2.0 传感器驱动。选择“是”。
3. 可在右侧托盘菜单选择“创建或更新桌面快捷方式”；公共桌面的快捷方式会刷新为 `V` 图标。
4. 在“设置 > 个性化 > 任务栏 > 任务栏行为”中，把“任务栏对齐方式”设为“靠左”。
5. 在同一页面把“合并任务栏按钮并隐藏标签”设为“从不”。
6. 程序启动后默认最小化，任务栏按钮标题会实时显示 `最大 1.234 V`。
7. 若要让左侧图标持续显示真实电压，请不要固定该按钮；运行中的按钮可以手动拖到开始按钮后的第一个应用位置。
8. 右侧通知区域图标保留设置菜单；若它被折叠，请在“设置 > 个性化 > 任务栏 > 其他系统托盘图标”中将它打开。

如果此前已经固定过，请先取消固定并重新运行程序，否则 Explorer 会继续显示缓存的静态图标。

Windows 11 固定后的主图标由 Explorer 缓存，不能每秒可靠替换，因此“固定位置”和“左侧主图标实时变化”无法同时保证。程序会持续更新未固定的左侧运行图标、左侧标题和右侧托盘图标；系统不允许应用自动改变按钮顺序或放到开始按钮之前。

读取传感器需要管理员权限，因此每次手动双击都会出现 UAC；通过程序创建的开机
计划任务会直接以最高权限启动，不会在登录时重复弹窗。当前登录账户需要本身属于
管理员组；如果标准用户在 UAC 中输入另一个管理员账户，计划任务会属于该管理员，
无法在标准用户的桌面显示托盘图标。

开机启动记录的是 EXE 的完整路径。如果之后移动了 EXE，请重新运行并再次打开
“开机自动启动”，程序会修复计划任务中的路径。

## 数值规则

LibreHardwareMonitor 0.9.6 对 13600K（Raptor Lake）通常提供 6 个 P-Core 和
8 个 E-Core 的逐核心 VID。程序不硬编码核心数量，BIOS 禁用核心后也能正常工作。

- 只统计当前仍有效的逐核心 VID，取其中最大值。
- 没有有效逐核心项目时，当前值显示 `--`；若此前已记录全局最大值，托盘会继续显示该峰值。
- 聚合 `CPU Core` 不是跨核心最大值，因此不会用作替代。
- 频率取所有逐核心 `Clock` 的每秒采样最大值，不包含 `Bus Speed`。
- 温度只取 LibreHardwareMonitor 的 `Core Max`，不包含 `Distance to TjMax`。
- 功率只取 RAPL `CPU Package`；它是约一秒窗口的平均封装功率，首次短窗口样本不计入峰值。
- “重置全部最大值”会同时重置电压、频率、温度和功率。
- 不读取主板 `Vcore`、`+12V` 或 WMI `CurrentVoltage`，避免混入语义不同或非实时的数据。

驱动注册信息存在但仍无读数时，右键菜单会保留“修复传感器驱动”入口。安全软件、
Windows 驱动策略或一次需要重启的安装也可能暂时阻止 PawnIO 设备加载。

## 测试

纯筛选逻辑不依赖 Windows，可运行：

```powershell
dotnet run --project .\tests\CpuCoreVoltageTray.LogicTests\CpuCoreVoltageTray.LogicTests.csproj -c Release
```

传感器读数、PawnIO 驱动和托盘行为必须在目标 Windows 11 机器上验证。可用官方
LibreHardwareMonitor 或 HWiNFO 对照空闲及负载状态下的 Core VID。

第三方组件及固定版本见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
