param(
    [string]$SdkPath,
    [string]$Distribution = 'BigBlueFish-Verification',
    [ValidateSet('Standard','Safe')][string]$Edition = 'Standard',
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [string]$WorkDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = & "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
$version = & "$PSScriptRoot/Get-ProjectVersion.ps1"
if (!$WorkDirectory) { $WorkDirectory = Join-Path $root ".work/linux-$Edition" }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'release' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
if (Test-Path -LiteralPath $work) { throw "构建目录已存在，请使用新的目录：$work" }
if (!(Test-Path -LiteralPath (Join-Path $runtime 'llama-server'))) { throw '请提供已核对的 Linux x64 llama.cpp 运行时目录。' }
New-Item -ItemType Directory -Path $work | Out-Null
New-Item -ItemType Directory -Path $output -ErrorAction SilentlyContinue | Out-Null
$payload = Join-Path $work 'payload'
& $sdk publish "$root/code/BigBlueFish.Linux/BigBlueFish.Linux.csproj" -c Release -r linux-x64 --self-contained true "-p:CompanionEdition=$Edition" -p:RestoreLockedMode=true -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw 'Linux 程序构建失败。' }
if ($Edition -eq 'Standard') {
    & $sdk publish "$root/code/BigBlueFish.Mcp/BigBlueFish.Mcp.csproj" -c Release -r linux-x64 --self-contained true -p:RestoreLockedMode=true -o $payload --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Linux MCP 组件构建失败。' }
}
function LinuxPath([string]$path) {
    $result = & wsl -d $Distribution -- wslpath -a ($path.Replace('\','/'))
    if ($LASTEXITCODE -ne 0) { throw "无法访问 Linux 构建路径：$path" }
    return ($result | Select-Object -Last 1).Trim()
}
& wsl -d $Distribution -- bash (LinuxPath "$PSScriptRoot/build-linux-deb.sh") (LinuxPath $root) (LinuxPath $work) (LinuxPath $runtime) (LinuxPath $output) $version $Edition
if ($LASTEXITCODE -ne 0) { throw 'Linux 安装包生成失败。' }
