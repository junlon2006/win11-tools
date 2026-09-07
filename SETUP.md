# Windows 安装包

安装包使用 Inno Setup 6 构建。生成前需要安装：

- .NET 8 SDK
- Inno Setup 6.3 或更高版本

在 PowerShell 中安装 Inno Setup：

```powershell
winget install --id JRSoftware.InnoSetup -e
```

先从右侧托盘菜单退出正在运行的程序，然后在仓库根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-setup.ps1
```

安装包输出到：

```text
%LOCALAPPDATA%\CpuCoreVoltageTraySetup\CpuCoreVoltageTray-Setup.exe
```

运行安装包：

```powershell
& "$env:LOCALAPPDATA\CpuCoreVoltageTraySetup\CpuCoreVoltageTray-Setup.exe"
```

构建脚本还会尽力复制一份到当前用户桌面：`CpuCoreVoltageTray-Setup.exe`。目标 Windows 11 电脑只需双击该安装包，不需要安装 .NET SDK 或 Inno Setup。

原始安装包始终保留在上述 LOCALAPPDATA 目录。

安装器会将程序安装到 `%ProgramFiles%\CpuCoreVoltageTray`，创建开始菜单快捷方式，
并提供以下选项：

- “登录 Windows 时自动启动”：默认选中。
- “为所有用户创建桌面快捷方式”：默认不选中。
- 安装完成后启动程序：默认选中。

自动启动使用当前用户登录触发、最高权限的 Windows 计划任务。它不会在每次登录时弹出
UAC。任务栏程序必须运行在交互式用户会话中，因此这里是“用户登录时启动”，不是登录
界面出现前运行的系统服务。计划任务会在登录后约延迟 10 秒启动，以等待 Explorer 通知区域就绪。
当前登录账户应本身属于管理员组；若标准用户在 UAC 中输入另一管理员账户，计划任务会属于该管理员。

卸载程序会删除自动启动计划任务、开始菜单项和产品桌面快捷方式。PawnIO 驱动不会随
程序卸载，因为它可能同时被其他硬件监控软件使用，可在 Windows“已安装的应用”中单独
管理。

安装包和应用目前没有代码签名证书，Windows SmartScreen 可能显示“未知发布者”。
