# Dev-only: compile build\DevShot.cs together with src\*.cs (minus Program.cs) and run it,
# producing PNG renders of the dialogs into build\ so control layout can be reviewed.
# Not part of the shipped app. Keep ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI).

$ErrorActionPreference = 'Stop'

$root   = Split-Path $PSScriptRoot -Parent
$tools  = Join-Path $root 'build\tools'
$objDir = Join-Path $root 'build\obj'
$fw     = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc    = Join-Path $tools 'roslyn\tasks\net472\csc.exe'

$sshnet   = Join-Path $tools 'sshnet2024\lib\net462\Renci.SshNet.dll'
$bclAsync = Join-Path $tools 'bclasync\lib\net461\Microsoft.Bcl.AsyncInterfaces.dll'
$taskExt  = Join-Path $tools 'extensions\lib\netstandard2.0\System.Threading.Tasks.Extensions.dll'
$unsafe   = Join-Path $tools 'unsafe\lib\netstandard2.0\System.Runtime.CompilerServices.Unsafe.dll'

$refs = @(
    (Join-Path $fw 'mscorlib.dll'),
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Drawing.dll'),
    (Join-Path $fw 'System.Windows.Forms.dll'),
    (Join-Path $fw 'System.Xml.dll'),
    (Join-Path $fw 'System.Xml.Linq.dll'),
    (Join-Path $fw 'System.Security.dll'),
    (Join-Path $fw 'netstandard.dll'),
    $sshnet, $bclAsync, $taskExt, $unsafe
)

$sources = Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.cs' |
    Where-Object { $_.Name -ne 'Program.cs' } |
    ForEach-Object { $_.FullName }
$sources += (Join-Path $root 'build\DevShot.cs')

$out = Join-Path $objDir 'devshot.exe'
$cscArgs = @('/nologo', '/noconfig', '/target:exe', '/platform:anycpu', '/optimize-', '/debug-', "/out:$out")
foreach ($r in $refs) { $cscArgs += "/reference:$r" }
$cscArgs += $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "devshot compile failed (exit code $LASTEXITCODE)" }

& $out (Join-Path $root 'build')
if ($LASTEXITCODE -ne 0) { throw "devshot run failed (exit code $LASTEXITCODE)" }
Write-Host 'SHOTS OK'
