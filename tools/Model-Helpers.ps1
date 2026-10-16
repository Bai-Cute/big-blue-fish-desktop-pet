# ModelStore.cs uses the same source catalog and selection format. ModelScriptsVerification.ps1 checks parity.
function Get-ModelSources {
    @(
        [pscustomobject]@{
            Id='huggingface'; Label='Hugging Face（推荐）'; FileName='Qwen3.5-4B-heretic-Q4_K_M.gguf'
            Url='https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true'
            Page='https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/tree/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1'
            Sha256='8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1'
        }
        [pscustomobject]@{
            Id='modelscope'; Label='国内源（稳定）'; FileName='qwen3-5-4B-Q4_K_M.gguf'
            Url='https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/resolve/124e4a76470ea89369ecddc493a8c1d75e3c9498/qwen3-5-4B-Q4_K_M.gguf'
            Page='https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/files'
            Sha256='de8e96cd0d0c358487091aaaed1346bc02e61da3d4b412c833662702e233e78c'
        }
    )
}

function Test-ModelGguf([string]$Path) {
    $reader=$null
    try {
        $reader=[IO.BinaryReader]::new([IO.File]::OpenRead($Path))
        return $reader.BaseStream.Length -ge 24 -and $reader.ReadUInt32() -eq 0x46554747 -and
            $reader.ReadUInt32() -gt 0 -and $reader.ReadUInt64() -gt 0 -and $reader.ReadUInt64() -gt 0
    } catch { return $false } finally { if($reader){$reader.Dispose()} }
}

function Read-ModelSelection([string]$Directory) {
    try { Get-Content -LiteralPath (Join-Path $Directory 'model-selection.json') -Raw -Encoding utf8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { return $null }
}

function Save-ModelSelection([string]$Directory,[object]$Selection) {
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $path=Join-Path $Directory 'model-selection.json'
    [IO.File]::WriteAllText($path+'.tmp',($Selection | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    if([IO.File]::Exists($path)){[IO.File]::Replace($path+'.tmp',$path,[NullString]::Value)}else{[IO.File]::Move($path+'.tmp',$path)}
}

function Resolve-DevelopmentModel([string]$ModelsDirectory,[switch]$NoModel) {
    $ModelsDirectory=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ModelsDirectory)
    $parent=[IO.DirectoryInfo]::new($ModelsDirectory).Parent
    $selection=if($parent){Read-ModelSelection $parent.FullName}else{$null}
    if($NoModel -or $selection.Source -ceq 'none'){
        return [pscustomobject]@{ Source='none'; FileName=$null; Sha256=$null }
    }
    if($null -ne $selection.FileName){
        $name=[string]$selection.FileName
        if([IO.Path]::GetFileName($name) -cne $name -or !(Test-ModelGguf (Join-Path $ModelsDirectory $name))){
            throw "选择记录中的模型不可用：$name。请运行 Setup-Model.ps1 准备模型，或使用 -NoModel。"
        }
        return $selection
    }
    $sources=Get-ModelSources
    foreach($name in @($sources[0].FileName,'Qwen3.5-4B-heretic-f16_Q4_K_M.gguf',$sources[1].FileName)){
        if(Test-ModelGguf (Join-Path $ModelsDirectory $name)){
            $source=if($name -eq $sources[1].FileName){$sources[1]}else{$sources[0]}
            return [pscustomobject]@{ Source=$source.Id; FileName=$name; Sha256=$null }
        }
    }
    throw '没有找到已选择的 GGUF 模型。请运行 Setup-Model.ps1 准备模型，或使用 -NoModel。'
}

function Set-DevelopmentJunction([string]$Path,[string]$Target) {
    $resolved=(Resolve-Path -LiteralPath $Target).Path
    $existing=Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if($existing){
        if($existing.LinkType -ne 'Junction'){throw "目录已存在且不是开发目录连接，请检查：$Path"}
        if([IO.Path]::GetFullPath([string]$existing.Target) -ieq $resolved){return}
        # Delete only the junction itself, never recurse into the shared runtime/model directory.
        [IO.Directory]::Delete($existing.FullName)
    }
    New-Item -ItemType Junction -Path $Path -Target $resolved | Out-Null
}
