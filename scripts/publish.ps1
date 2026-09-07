param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\CpuCoreVoltageTray\CpuCoreVoltageTray.csproj'
$sourceIcon = Join-Path $repositoryRoot 'src\CpuCoreVoltageTray\Resources\app.ico'
$repositoryOutput = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\win-x64'))
$localOutput = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $null
}
else {
    [System.IO.Path]::GetFullPath(
        (Join-Path $env:LOCALAPPDATA 'CpuCoreVoltageTray'))
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if ($null -eq $localOutput) {
        throw '无法确定当前用户的 LOCALAPPDATA 目录'
    }

    $output = $localOutput
}
else {
    $output = [System.IO.Path]::GetFullPath(
        $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
            $OutputDirectory))
}

$isLocalOutput = $null -ne $localOutput -and [string]::Equals(
    $output,
    $localOutput,
    [System.StringComparison]::OrdinalIgnoreCase)
$isRepositoryOutput = [string]::Equals(
    $output,
    $repositoryOutput,
    [System.StringComparison]::OrdinalIgnoreCase)
if (-not $isLocalOutput -and -not $isRepositoryOutput) {
    throw "拒绝清理未授权的输出目录：$output"
}

$executable = Join-Path $output 'CpuCoreVoltageTray.exe'
$publishedIcon = Join-Path $output 'app.ico'

Get-Command dotnet -ErrorAction Stop | Out-Null
$runningProcess = Get-Process -Name CpuCoreVoltageTray -ErrorAction SilentlyContinue
if ($null -ne $runningProcess) {
    throw "请先从右侧托盘菜单退出正在运行的 CpuCoreVoltageTray，再重新发布"
}

if (-not (Test-Path -LiteralPath $sourceIcon -PathType Leaf)) {
    throw "找不到快捷方式图标源文件：$sourceIcon"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

& dotnet clean $project --configuration Release
$cleanExitCode = $LASTEXITCODE
if ($cleanExitCode -ne 0) {
    throw "dotnet clean 失败，退出代码：$cleanExitCode"
}

& dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $output

$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "dotnet publish 失败，退出代码：$publishExitCode"
}

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "发布命令成功结束，但没有生成程序：$executable"
}

Copy-Item -LiteralPath $sourceIcon -Destination $publishedIcon -Force
if (-not (Test-Path -LiteralPath $publishedIcon -PathType Leaf)) {
    throw "发布命令成功结束，但没有生成快捷方式图标：$publishedIcon"
}

Copy-Item (Join-Path $repositoryRoot 'README.md') $output -Force
Copy-Item (Join-Path $repositoryRoot 'SETUP.md') $output -Force
Copy-Item (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') $output -Force
Copy-Item (Join-Path $repositoryRoot 'licenses') $output -Recurse -Force

Write-Host ""
Write-Host "Published: $executable"
