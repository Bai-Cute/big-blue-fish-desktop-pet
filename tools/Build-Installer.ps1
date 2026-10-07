param(
    [string]$SdkPath,
    [string]$RuntimeDirectory,
    [string]$VisualCppDirectory,
    [string]$Version,
    [string]$OutputDirectory,
    [string]$WorkDirectory,
    [ValidateSet('Ocr','Vision')][string]$InputMode = 'Ocr'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/Setup-Media.ps1"
$Version = & "$PSScriptRoot/Get-ProjectVersion.ps1" -Version $Version
$sdk = & "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
if (!$WorkDirectory) { $WorkDirectory = Join-Path $root '.work' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$modeName = if ($InputMode -eq 'Ocr') { 'OCR' } else { 'Vision' }
$baseName = "BigBlueFish-v$Version-$modeName-Setup"
$payload = Join-Path $work "BigBlueFish-v$Version-$modeName-payload"
$stub = Join-Path $work "BigBlueFish-v$Version-$modeName-setup-stub"
$uninstaller = Join-Path $work "BigBlueFish-v$Version-$modeName-uninstaller"
$payloadZip = Join-Path $work "BigBlueFish-v$Version-$modeName-payload.zip"
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$output = Join-Path $OutputDirectory "$baseName.exe"
$shaFile = Join-Path $OutputDirectory "$baseName.sha256"
foreach ($p in @($payload,$stub,$payloadZip,$uninstaller)) {
    if (Test-Path -LiteralPath $p) { throw "中间产物已存在，请先检查后移走：$p" }
}
if (Test-Path -LiteralPath $output) { throw "发布文件已存在，请使用新的工作区或先移走：$output" }
New-Item -ItemType Directory -Force -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $payload | Out-Null
& $sdk publish "$root/code/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj" -c Release -p:Platform=x64 "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" "-p:InformationalVersion=$Version" -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw '桌宠发布失败。' }
$runtimeTarget = Join-Path $payload 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeTarget | Out-Null
if ($RuntimeDirectory) {
    if (!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'llama-server.exe'))) { throw '运行时目录缺少 llama-server.exe。' }
    Get-ChildItem -LiteralPath $RuntimeDirectory -Force | Copy-Item -Destination $runtimeTarget -Recurse
} else {
    throw '为避免未经核对的运行时进入正式安装器，请显式传入 -RuntimeDirectory。'
}
& "$PSScriptRoot/Copy-VisualCppRuntime.ps1" -Destination $runtimeTarget -SourceDirectory $VisualCppDirectory
Copy-Item -LiteralPath "$root/code/licenses" -Destination $payload -Recurse
Copy-Item -LiteralPath "$root/code/LICENSE", "$root/code/THIRD-PARTY.md" -Destination $payload
Copy-Item -LiteralPath "$root/release/README.md" -Destination (Join-Path $payload 'INSTALL-README.md')
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'models') | Out-Null
& $sdk publish "$root/uninstaller/BigBlueFish.Uninstall.csproj" -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" -o $uninstaller --nologo
if ($LASTEXITCODE -ne 0) { throw '卸载器编译失败。' }
Copy-Item -LiteralPath (Join-Path $uninstaller 'Uninstall.exe') -Destination $payload
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $payloadZip -CompressionLevel Optimal
New-Item -ItemType Directory -Force -Path $stub | Out-Null
& $sdk publish "$root/installer/BigBlueFish.Setup.csproj" -c Release "-p:CompanionInputMode=$InputMode" -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RestoreLockedMode=true "-p:Version=$Version" -o $stub --nologo
if ($LASTEXITCODE -ne 0) { throw '安装器编译失败。' }
$stubExe = Join-Path $stub 'BigBlueFish.Setup.exe'
if (!(Test-Path -LiteralPath $stubExe)) { throw '未找到安装器可执行文件。' }
$marker = [Text.Encoding]::ASCII.GetBytes('BIGBLUEFISH_PAYLOAD_V1')
$payloadLength = (Get-Item -LiteralPath $payloadZip).Length
$fs = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stubBytes = [IO.File]::ReadAllBytes($stubExe)
    $fs.Write($stubBytes, 0, $stubBytes.Length)
    $payloadBytes = [IO.File]::ReadAllBytes($payloadZip)
    $fs.Write($payloadBytes, 0, $payloadBytes.Length)
    $fs.Write($marker, 0, $marker.Length)
    $lengthBytes = [BitConverter]::GetBytes([Int64]$payloadLength)
    $fs.Write($lengthBytes, 0, $lengthBytes.Length)
} finally { $fs.Dispose() }
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $shaFile -Encoding ascii -Value "$hash  $baseName.exe"
Write-Host "安装器已生成：$output"
Write-Host "SHA256：$hash"
