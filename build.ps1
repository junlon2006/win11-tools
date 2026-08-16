[CmdletBinding()]
param(
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'Win11Monitor.sln'

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE"
    }
}

Invoke-DotNet restore $solution
Invoke-DotNet build $solution -c Release --no-restore
Invoke-DotNet test $solution -c Release --no-build

if ($Publish) {
    $output = Join-Path $repoRoot 'artifacts\Z690Monitor'
    Invoke-DotNet publish (Join-Path $repoRoot 'src\Win11Monitor.App\Win11Monitor.App.csproj') `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $output

    Write-Host "Published to $output"
}
