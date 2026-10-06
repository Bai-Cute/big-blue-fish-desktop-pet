param([string]$DestinationDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$DestinationDirectory){$DestinationDirectory=Join-Path $root 'code/VPet-Simulator.Windows/media'}
$executable=Join-Path $DestinationDirectory 'ffmpeg.exe'
if((Test-Path -LiteralPath $executable) -and (Test-Path -LiteralPath (Join-Path $DestinationDirectory 'FFmpeg-LICENSE.txt'))){return $executable}
function Get-ArchiveSha256([string]$Path){
    $algorithm=[Security.Cryptography.SHA256]::Create()
    $stream=[IO.File]::OpenRead($Path)
    try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
    finally{$stream.Dispose();$algorithm.Dispose()}
}
$cache=Join-Path $root '.work/media'
New-Item -ItemType Directory -Path $cache,$DestinationDirectory -Force | Out-Null
$archive=Join-Path $cache 'ffmpeg-9.0.1-essentials_build.zip'
$hash='fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9'
if(!(Test-Path -LiteralPath $archive)){
    $partial="$archive.partial"
    [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $download=[Net.WebClient]::new()
    try{$download.DownloadFile('https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip',$partial)}finally{$download.Dispose()}
    if((Get-ArchiveSha256 $partial) -ine $hash){throw '动画解码组件 SHA256 不匹配。'}
    Move-Item -LiteralPath $partial -Destination $archive
}
if((Get-ArchiveSha256 $archive) -ine $hash){throw '动画解码组件缓存 SHA256 不匹配。'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    $entry=$zip.Entries | Where-Object {$_.FullName -match '/bin/ffmpeg\.exe$'} | Select-Object -First 1
    if(!$entry){throw '动画解码组件中缺少 ffmpeg.exe。'}
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$executable,$true)
    $license=$zip.Entries | Where-Object {$_.FullName -match '/LICENSE$'} | Select-Object -First 1
    if(!$license){throw '动画解码组件中缺少许可证。'}
    [IO.Compression.ZipFileExtensions]::ExtractToFile($license,(Join-Path $DestinationDirectory 'FFmpeg-LICENSE.txt'),$true)
}finally{$zip.Dispose()}
return $executable
