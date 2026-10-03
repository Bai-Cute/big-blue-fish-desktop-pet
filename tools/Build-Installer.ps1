param(
    [string]$SdkPath,
    [string]$RuntimeDirectory,
    [string]$Version = '0.1.1',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw '版本号格式应为 0.1.0。' }
$sdk = & "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
$work = Join-Path $root '.work'
$payload = Join-Path $work "BigBlueFish-v$Version-payload"
$stub = Join-Path $work "BigBlueFish-v$Version-setup-stub"
$payloadZip = Join-Path $work "BigBlueFish-v$Version-payload.zip"
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$output = Join-Path $OutputDirectory 'BigBlueFish-Setup-x64.exe'
$shaFile = Join-Path $OutputDirectory 'BigBlueFish-Setup-x64.sha256'
foreach ($p in @($payload,$stub,$payloadZip)) {
    if (Test-Path -LiteralPath $p) { throw "中间产物已存在，请先检查后移走：$p" }
}
if (Test-Path -LiteralPath $output) { throw "发布文件已存在，请使用新的工作区或先移走：$output" }
New-Item -ItemType Directory -Force -Path $work | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $payload | Out-Null
& $sdk publish "$root/code/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:RestoreLockedMode=true "-p:Version=$Version" "-p:InformationalVersion=$Version" -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw '桌宠发布失败。' }
$runtimeTarget = Join-Path $payload 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeTarget | Out-Null
if ($RuntimeDirectory) {
    if (!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'llama-server.exe'))) { throw '运行时目录缺少 llama-server.exe。' }
    Get-ChildItem -LiteralPath $RuntimeDirectory -Force | Copy-Item -Destination $runtimeTarget -Recurse
} else {
    throw '为避免未经核对的运行时进入正式安装器，请显式传入 -RuntimeDirectory。'
}
Copy-Item -LiteralPath "$root/code/licenses" -Destination $payload -Recurse
Copy-Item -LiteralPath "$root/code/LICENSE", "$root/code/THIRD-PARTY.md" -Destination $payload
Copy-Item -LiteralPath "$root/tools/Setup-Model.ps1" -Destination $payload
Set-Content -LiteralPath (Join-Path $payload 'INSTALL-README.txt') -Encoding utf8 -Value @"
蓝色大肥鱼安装目录

本目录由 BigBlueFish-Setup-x64.exe 安装。
本地 4B 模型由安装程序从固定版本的 Hugging Face 来源下载并校验。
模型文件不属于 Git 仓库内容。
"@
New-Item -ItemType Directory -Force -Path (Join-Path $payload 'models') | Out-Null
Set-Content -LiteralPath (Join-Path $payload 'models/README.txt') -Encoding utf8 -Value '模型由 BigBlueFish 安装程序下载到此目录。'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $payloadZip -CompressionLevel Optimal
New-Item -ItemType Directory -Force -Path $stub | Out-Null
& $sdk publish "$root/installer/BigBlueFish.Setup.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:RestoreLockedMode=true "-p:Version=$Version" -o $stub --nologo
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
Set-Content -LiteralPath $shaFile -Encoding ascii -Value "$hash  BigBlueFish-Setup-x64.exe"
Write-Host "安装器已生成：$output"
Write-Host "SHA256：$hash"
