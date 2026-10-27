param(
    [ValidateSet('HuggingFace','ModelScope','Manual','None')][string]$Source='HuggingFace',
    [string]$ModelFile,
    [string]$DestinationDirectory=$PSScriptRoot,
    [switch]$AllowUnverifiedModel
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Model-Helpers.ps1"
$DestinationDirectory=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationDirectory)
$DestinationDirectory=Join-Path $DestinationDirectory 'data'
if($ModelFile -and !$PSBoundParameters.ContainsKey('Source')){$Source='Manual'}
if($ModelFile -and $Source -ne 'Manual'){throw '-ModelFile 仅用于手动安装，请使用 -Source Manual。'}
if($Source -eq 'None'){
    Save-ModelSelection $DestinationDirectory ([ordered]@{Source='none';FileName=$null;Sha256=$null})
    Write-Host '已选择无模型启动。'
    return
}
$sources=Get-ModelSources
$folder=Join-Path $DestinationDirectory 'models'
if($Source -eq 'Manual'){
    if(!$ModelFile){$ModelFile=Read-Host '请输入 GGUF 模型的完整路径'}
    $ModelFile=(Resolve-Path -LiteralPath $ModelFile).Path
    if(!(Test-ModelGguf $ModelFile)){throw '请选择有效的 GGUF 模型文件。'}
    $hash=(Get-FileHash -LiteralPath $ModelFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $known=$sources | Where-Object Sha256 -EQ $hash | Select-Object -First 1
    if(!$known){
        Write-Warning '这不是本软件期待的模型，您可以尝试使用，但稳定性无法保证'
        if(!$AllowUnverifiedModel -and (Read-Host '继续使用？输入 Y 确认') -ine 'Y'){throw '已取消手动安装。'}
    }
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $name=[IO.Path]::GetFileName($ModelFile)
    $target=Join-Path $folder $name
    if($ModelFile -ine $target){
        $temporary=$target+'.importing'
        Copy-Item -LiteralPath $ModelFile -Destination $temporary
        if(!(Test-ModelGguf $temporary) -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $hash){throw '模型文件复制未完成，请重试。'}
        if([IO.File]::Exists($target)){[IO.File]::Replace($temporary,$target,[NullString]::Value)}else{[IO.File]::Move($temporary,$target)}
    }
    $id=if($known){$known.Id}else{'manual'}
}else{
    $artifact=if($Source -eq 'ModelScope'){$sources[1]}else{$sources[0]}
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $name=$artifact.FileName
    $target=Join-Path $folder $name
    $hash=$artifact.Sha256
    if(Test-Path -LiteralPath $target){
        if(!(Test-ModelGguf $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $hash){
            throw "目标已有不同的模型文件，请检查后重试，或使用手动安装：$target"
        }
    }else{
        $temporary=$target+'.partial'
        Write-Host "正在从$($artifact.Label)下载本地 4B 模型：$($artifact.Page)"
        # A retry replaces only the incomplete download; the selection is committed after validation.
        Invoke-WebRequest -Uri $artifact.Url -OutFile $temporary
        if(!(Test-ModelGguf $temporary) -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $hash){throw '模型校验失败，保留 .partial 文件供检查。'}
        [IO.File]::Move($temporary,$target)
    }
    $id=$artifact.Id
}
Save-ModelSelection $DestinationDirectory ([ordered]@{Source=$id;FileName=$name;Sha256=$hash})
Write-Host "本地模型已就绪：$target"
