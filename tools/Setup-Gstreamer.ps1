param([string]$DestinationDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$DestinationDirectory){$DestinationDirectory=Join-Path $root 'code/BigBlueFish.Windows/media/gstreamer'}
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gstreamer-runtime.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cache=Join-Path $root '.work/gstreamer-download'
New-Item -ItemType Directory -Path $cache,$DestinationDirectory -ErrorAction SilentlyContinue | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function FileHash([string]$Path){
    $algorithm=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path)
    try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()}
    finally{$stream.Dispose();$algorithm.Dispose()}
}
foreach($archive in $manifest.archives){
    $missing=@($archive.files | Where-Object {
        $path=Join-Path $DestinationDirectory $_.target
        !(Test-Path -LiteralPath $path) -or (FileHash $path) -ine $_.sha256
    })
    if($missing.Count -eq 0){continue}
    $zipPath=Join-Path $cache ([IO.Path]::GetFileName(([Uri]$archive.url).AbsolutePath))
    if(!(Test-Path -LiteralPath $zipPath)){
        $partial="$zipPath.partial"
        [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $client=[Net.WebClient]::new()
        try{$client.DownloadFile($archive.url,$partial)}finally{$client.Dispose()}
        if((FileHash $partial) -ine $archive.sha256){throw '动画组件下载校验失败。'}
        Move-Item -LiteralPath $partial -Destination $zipPath
    }
    if((FileHash $zipPath) -ine $archive.sha256){throw '动画组件缓存校验失败。'}
    $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
    try{
        foreach($item in $missing){
            $entry=$zip.GetEntry($item.source)
            if(!$entry){throw "动画组件缺失：$($item.source)"}
            $target=Join-Path $DestinationDirectory $item.target
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
            if((FileHash $target) -ine $item.sha256){throw "动画组件文件校验失败：$($item.target)"}
        }
    }finally{$zip.Dispose()}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'gstreamer-runtime.json') -Destination (Join-Path $DestinationDirectory 'runtime-manifest.json')
