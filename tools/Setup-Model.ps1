param([string]$ModelFile,[string]$DestinationDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$name='Qwen3.5-4B-heretic-Q4_K_M.gguf'
$sha='8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1'
$folder=Join-Path $DestinationDirectory 'models'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$target=Join-Path $folder $name
if(Test-Path -LiteralPath $target){
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ieq $sha){Write-Host '模型已就绪。'; return}
    throw '目标已有不同的模型文件，请移走后重试。'
}
$temporary=Join-Path $folder ($name+'.partial')
if(Test-Path -LiteralPath $temporary){throw '存在未完成的 .partial 文件，请检查后删除或移走，再重试。'}
if($ModelFile){
    if((Get-FileHash -LiteralPath $ModelFile -Algorithm SHA256).Hash -ine $sha){throw '所选模型 SHA256 不匹配。'}
    Copy-Item -LiteralPath $ModelFile -Destination $temporary
}else{
    Write-Host '正在下载约 2.7 GB 模型，请等待。'
    $url='https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true'
    Invoke-WebRequest -Uri $url -OutFile $temporary
}
if((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $sha){throw '模型校验失败，保留 .partial 文件供检查。'}
Move-Item -LiteralPath $temporary -Destination $target
Write-Host '模型已就绪，可以启动蓝色大肥鱼。'
