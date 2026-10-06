param([string]$ModelFile,[string]$DestinationDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$artifacts = @(
    @{ Name='Qwen3.5-4B-heretic-Q4_K_M.gguf'; Sha='8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1'; Url='https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true'; Label='本地 4B 模型' },
    @{ Name='Qwen3.5-4B-heretic.mmproj-f16.gguf'; Sha='e638dc8de3b75309a190092ba006307759343b62ae0d21ed8359df76b9b76c3b'; Url='https://huggingface.co/mradermacher/Qwen3.5-4B-heretic-GGUF/resolve/0d92f575bfcb057411f3d4088c5eabed979a9b3f/Qwen3.5-4B-heretic.mmproj-f16.gguf?download=true'; Label='视觉投影组件' }
)
$folder=Join-Path $DestinationDirectory 'models'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
foreach($artifact in $artifacts){
    $target=Join-Path $folder $artifact.Name
    if(Test-Path -LiteralPath $target){
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ieq $artifact.Sha){Write-Host "$($artifact.Label)已就绪。"; continue}
        throw "目标已有不同的$($artifact.Label)文件，请移走后重试。"
    }
    $temporary=Join-Path $folder ($artifact.Name+'.partial')
    if(Test-Path -LiteralPath $temporary){throw "存在未完成的 .partial 文件，请检查后删除或移走，再重试：$temporary"}
    if($ModelFile -and $artifact.Name -eq $artifacts[0].Name){
        if((Get-FileHash -LiteralPath $ModelFile -Algorithm SHA256).Hash -ine $artifact.Sha){throw '所选模型 SHA256 不匹配。'}
        Copy-Item -LiteralPath $ModelFile -Destination $temporary
    }else{
        Write-Host "正在下载$($artifact.Label)，请等待。"
        Invoke-WebRequest -Uri $artifact.Url -OutFile $temporary
    }
    if((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $artifact.Sha){throw "$($artifact.Label)校验失败，保留 .partial 文件供检查。"}
    Move-Item -LiteralPath $temporary -Destination $target
}
Write-Host '模型与视觉投影组件已就绪，可以启动蓝色大肥鱼。'
