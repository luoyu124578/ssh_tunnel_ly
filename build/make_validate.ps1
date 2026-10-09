# Dev-only: compile build\probe_validate.cs together with the model/config sources and run it,
# printing a table of TunnelConfig.Validate() results (blank addresses, wildcard addresses,
# bad host characters, out-of-range ports, bad usernames).
# Not part of the shipped app. Keep ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI).

$ErrorActionPreference = 'Stop'

$root   = Split-Path $PSScriptRoot -Parent
$tools  = Join-Path $root 'build\tools'
$objDir = Join-Path $root 'build\obj'
$fw     = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc    = Join-Path $tools 'roslyn\tasks\net472\csc.exe'

$refs = @(
    (Join-Path $fw 'mscorlib.dll'),
    (Join-Path $fw 'System.dll'),
    (Join-Path $fw 'System.Core.dll'),
    (Join-Path $fw 'System.Xml.dll'),
    (Join-Path $fw 'System.Security.dll'),
    (Join-Path $fw 'netstandard.dll')
)

$sources = @(
    (Join-Path $root 'src\Model.cs'),
    (Join-Path $root 'src\Json.cs'),
    (Join-Path $root 'src\ConfigStore.cs'),
    (Join-Path $root 'src\Log.cs'),
    (Join-Path $root 'build\probe_validate.cs')
)

$out = Join-Path $objDir 'probe_validate.exe'
$cscArgs = @('/nologo', '/noconfig', '/target:exe', '/platform:anycpu', '/optimize+', '/debug-', "/out:$out")
foreach ($r in $refs) { $cscArgs += "/reference:$r" }
$cscArgs += $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "probe_validate compile failed (exit code $LASTEXITCODE)" }

& $out
if ($LASTEXITCODE -ne 0) { throw "probe_validate run failed (exit code $LASTEXITCODE)" }
