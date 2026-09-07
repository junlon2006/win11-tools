$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot 'publish.ps1'
$installerScript = Join-Path $repositoryRoot 'installer\CpuCoreVoltageTray.iss'

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw '无法确定当前用户的 LOCALAPPDATA 目录'
}

$localAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
$publishDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $localAppData 'CpuCoreVoltageTray'))
$setupOutputDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $localAppData 'CpuCoreVoltageTraySetup'))
$setupExecutable = Join-Path $setupOutputDirectory 'CpuCoreVoltageTray-Setup.exe'

$setupOutputParent = [System.IO.Directory]::GetParent($setupOutputDirectory)
$isExactSetupOutput = $null -ne $setupOutputParent -and
    [string]::Equals(
        $setupOutputParent.FullName.TrimEnd('\', '/'),
        $localAppData.TrimEnd('\', '/'),
        [System.StringComparison]::OrdinalIgnoreCase) -and
    [string]::Equals(
        [System.IO.Path]::GetFileName($setupOutputDirectory),
        'CpuCoreVoltageTraySetup',
        [System.StringComparison]::OrdinalIgnoreCase)
if (-not $isExactSetupOutput) {
    throw "拒绝清理非预期的安装包输出目录：$setupOutputDirectory"
}

if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
    throw "找不到发布脚本：$publishScript"
}

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "找不到 Inno Setup 配置：$installerScript"
}

$isccPath = $null
$isccCommand = Get-Command 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $isccCommand) {
    $isccPath = $isccCommand.Path
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    $isccCandidates = @(
        (Join-Path $localAppData 'Programs\Inno Setup 6\ISCC.exe')
    )
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $isccCandidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $isccCandidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
    }

    foreach ($candidate in $isccCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $isccPath = $candidate
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    $uninstallKeys = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )

    foreach ($uninstallKey in $uninstallKeys) {
        $installation = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
        if ($null -eq $installation -or
            [string]::IsNullOrWhiteSpace($installation.InstallLocation)) {
            continue
        }

        $candidate = Join-Path $installation.InstallLocation 'ISCC.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $isccPath = $candidate
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "未找到 Inno Setup 6.3 或更高版本编译器 ISCC.exe。请检查 %LOCALAPPDATA%\Programs\Inno Setup 6，或运行：winget install JRSoftware.InnoSetup"
}

Write-Host "Inno Setup: $isccPath"
& $publishScript

if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'CpuCoreVoltageTray.exe') -PathType Leaf)) {
    throw "发布脚本已结束，但没有找到程序：$publishDirectory\CpuCoreVoltageTray.exe"
}

if (Test-Path -LiteralPath $setupOutputDirectory) {
    Remove-Item -LiteralPath $setupOutputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $setupOutputDirectory | Out-Null

$isccArguments = @(
    "/DPublishDir=$publishDirectory"
    "/DSetupOutputDir=$setupOutputDirectory"
    $installerScript
)

& $isccPath @isccArguments
$isccExitCode = $LASTEXITCODE
if ($isccExitCode -ne 0) {
    throw "Inno Setup 编译失败，退出代码：$isccExitCode"
}

if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) {
    throw "Inno Setup 编译成功结束，但没有生成安装包：$setupExecutable"
}

Write-Host ""
Write-Host "Setup: $setupExecutable"
try {
    $desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
        -not [System.IO.Directory]::Exists($desktopDirectory)) {
        throw "无法确定当前用户的桌面目录"
    }

    $desktopSetup = Join-Path $desktopDirectory "CpuCoreVoltageTray-Setup.exe"
    Copy-Item -LiteralPath $setupExecutable -Destination $desktopSetup -Force -ErrorAction Stop
    Write-Host "Desktop setup: $desktopSetup"
}
catch {
    Write-Warning "安装包已成功生成，但无法复制到桌面；原始位置：$setupExecutable"
    Write-Warning $_.Exception.Message
}
