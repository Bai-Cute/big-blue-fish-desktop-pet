param(
    [ValidateSet('Ocr', 'Vision')][string]$InputMode = 'Ocr',
    [Parameter(Mandatory = $true)][string]$SdkPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = & "$PSScriptRoot/Get-ProjectVersion.ps1" -InputMode $InputMode
$destination = Join-Path $root ".work/BigBlueFish-v$version-$($InputMode.ToLowerInvariant())"
& $SdkPath publish "$root/code/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj" -c Release `
    -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw '本地对照版本构建失败。' }
$actual = (Get-Item -LiteralPath (Join-Path $destination 'VPet-Simulator.Windows.exe')).VersionInfo.ProductVersion
if ($actual -ne $version) { throw "构建版本不一致：期望 $version，实际 $actual。" }
Write-Output $destination
