$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$files=@(& git -C $root ls-files --cached --others --exclude-standard -- '*.ps1')
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate PowerShell scripts.'}
foreach($relative in $files){
    $path=Join-Path $root $relative
    $bytes=[IO.File]::ReadAllBytes($path)
    if($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191){throw "Missing UTF-8 BOM: $relative"}
    $tokens=$null; $errors=$null
    [System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors) | Out-Null
    if($errors.Count -gt 0){throw "PowerShell parsing failed: $relative"}
}
Write-Output "RESULT $($files.Count) PowerShell scripts have UTF-8 BOM and parse on PowerShell $($PSVersionTable.PSVersion)."
