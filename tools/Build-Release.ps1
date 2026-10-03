param([string]$SdkPath,[string]$RuntimeDirectory,[string]$RuntimeZip,[string]$Version='0.2.2')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$'){throw '版本号格式应为 0.2.0 或 0.2.0-test1。'}
$name="BigBlueFish-v$Version-win-x64"
$destination=Join-Path $root "release/$name"
if((Test-Path -LiteralPath $destination) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)){throw "发布目录已存在：$destination。请使用新的版本号或先自行移走该目录。"}
$sdk=& "$root/code/tools/Resolve-Sdk.ps1" -SdkPath $SdkPath
New-Item -ItemType Directory -Force -Path $destination | Out-Null
& $sdk publish "$root/code/VPet-Simulator.Windows/VPet-Simulator.Windows.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:RestoreLockedMode=true -o $destination --nologo
if($LASTEXITCODE -ne 0){throw '发布编译失败。未完成的发布目录保留供检查。'}
$runtimeDestination=Join-Path $destination 'runtime'
if($RuntimeDirectory){
    if(!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'llama-server.exe'))){throw '推理运行时目录中缺少 llama-server.exe。'}
    New-Item -ItemType Directory -Path $runtimeDestination | Out-Null
    foreach($item in Get-ChildItem -LiteralPath $RuntimeDirectory){Copy-Item -LiteralPath $item.FullName -Destination $runtimeDestination -Recurse}
}else{
    $cache=Join-Path $root '.work'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    if(!$RuntimeZip){
        $RuntimeZip=Join-Path $cache 'llama-b10809-bin-win-vulkan-x64.zip'
        if(!(Test-Path -LiteralPath $RuntimeZip)){
            Invoke-WebRequest -Uri 'https://github.com/ggml-org/llama.cpp/releases/download/b10809/llama-b10809-bin-win-vulkan-x64.zip' -OutFile $RuntimeZip
        }
    }
    if((Get-FileHash -LiteralPath $RuntimeZip -Algorithm SHA256).Hash -ine '97e50b3ef0cdd2cb4d5afd446a9006b3496bee6c0d0ba7083d32f36075771870'){throw 'llama.cpp 发布包 SHA256 不匹配。'}
    Expand-Archive -LiteralPath $RuntimeZip -DestinationPath $runtimeDestination
}
if(!(Test-Path -LiteralPath (Join-Path $runtimeDestination 'llama-server.exe'))){throw '发布包缺少 llama-server.exe。'}
Copy-Item -LiteralPath "$root/code/licenses" -Destination $destination -Recurse
Copy-Item -LiteralPath "$root/code/LICENSE","$root/code/THIRD-PARTY.md" -Destination $destination
Copy-Item -LiteralPath "$root/tools/Setup-Model.ps1" -Destination $destination
Copy-Item -LiteralPath "$root/release/README.md" -Destination (Join-Path $destination 'README.md')
New-Item -ItemType Directory -Path (Join-Path $destination 'models') | Out-Null
Set-Content -LiteralPath (Join-Path $destination 'models/README.txt') -Value '运行 Setup-Model.ps1 配置模型。模型文件不随 Git 仓库或此发布包分发。' -Encoding utf8
$files=Get-ChildItem -LiteralPath $destination -Recurse -File
$manifest=foreach($file in $files){
    [ordered]@{path=[IO.Path]::GetRelativePath($destination,$file.FullName).Replace('\','/');size=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[ordered]@{version=$Version;platform='win-x64';selfContained=$true;modelIncluded=$false;files=$manifest} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
$archive=Join-Path $root "release/$name.zip"
Compress-Archive -LiteralPath $destination -DestinationPath $archive
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $root "release/$name.sha256") -Value "$hash  $name.zip" -Encoding ascii
Write-Host "发布包已生成：$archive"
