param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $root 'code/BigBlueFish.Windows/media/gstreamer'}
$source=Join-Path $root 'code/native/Composition.cpp'
$target=Join-Path $OutputDirectory 'BigBlueFish.Composition.dll'
if((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).LastWriteTimeUtc -ge (Get-Item -LiteralPath $source).LastWriteTimeUtc){return}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw '请安装 Visual Studio C++ x64 构建工具。'}
$work=Join-Path $root '.work/composition-native'
[IO.Directory]::CreateDirectory($work)|Out-Null
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
$batch=Join-Path $work 'build.cmd'
$lines=@('@echo off',('call "'+$vs+'\VC\Auxiliary\Build\vcvars64.bat" >nul'),('cl /nologo /utf-8 /std:c++17 /EHsc /O2 /MD /LD "'+$source+'" /Fe:"'+$target+'" /Fo:"'+$work+'\Composition.obj" /link /IMPLIB:"'+$work+'\Composition.lib" dcomp.lib user32.lib gdi32.lib'))
[IO.File]::WriteAllLines($batch,$lines,[Text.UTF8Encoding]::new($false))
& cmd.exe /d /c $batch
if($LASTEXITCODE -ne 0){throw 'GPU 合成组件构建失败。'}
