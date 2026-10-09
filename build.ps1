# ssh_tunnel_ly build script
#   1) compile src\*.cs into a .NET Framework 4.8 WinForms exe using the NuGet-fetched Roslyn (csc.exe)
#   2) merge SSH.NET and its dependencies into one single exe with ILRepack
# Output: bin\ssh_tunnel_ly.exe  (single file, no side-by-side DLLs, never drops files next to itself)
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#         (the machine execution policy is Restricted, so "& .\build.ps1" is refused)
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 as ANSI (GBK here)
#       unless a UTF-8 BOM is present, which corrupts non-ASCII literals.

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$tools   = Join-Path $root 'build\tools'
$objDir  = Join-Path $root 'build\obj'
$binDir  = Join-Path $root 'bin'
$fw      = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

$csc      = Join-Path $tools 'roslyn\tasks\net472\csc.exe'
$ilrepack = Join-Path $tools 'ilrepack\tools\ILRepack.exe'
$icon     = Join-Path $root 'build\app.ico'
$manifest = Join-Path $root 'src\app.manifest'

$sshnet   = Join-Path $tools 'sshnet2024\lib\net462\Renci.SshNet.dll'
$bclAsync = Join-Path $tools 'bclasync\lib\net461\Microsoft.Bcl.AsyncInterfaces.dll'
$taskExt  = Join-Path $tools 'extensions\lib\netstandard2.0\System.Threading.Tasks.Extensions.dll'
$unsafe   = Join-Path $tools 'unsafe\lib\netstandard2.0\System.Runtime.CompilerServices.Unsafe.dll'

foreach ($p in @($csc, $ilrepack, $sshnet, $bclAsync, $taskExt, $unsafe)) {
    if (-not (Test-Path $p)) { throw "missing build dependency: $p" }
}
if (-not (Test-Path $icon)) {
    Write-Host 'icon not found, generating build\app.ico ...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build\make_icon.ps1') | Out-Null
}

New-Item -ItemType Directory -Force -Path $objDir | Out-Null
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

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

$sources = Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$primary = Join-Path $objDir 'ssh_tunnel_ly.exe'

# /deterministic+ : same source + same paths = byte-identical exe, so a hash that an AV
#                   vendor has once whitelisted stays whitelisted across rebuilds.
# /pathmap        : keep local absolute paths out of the assembly metadata.
$cscArgs = @(
    '/nologo', '/noconfig', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-', '/warn:4',
    '/deterministic+',
    "/pathmap:$root\=/_/",
    "/out:$primary",
    "/win32icon:$icon",
    "/win32manifest:$manifest"
)
foreach ($r in $refs) { $cscArgs += "/reference:$r" }
$cscArgs += $sources

Write-Host '== [1/2] compile ==' -ForegroundColor Cyan
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed (csc exit code $LASTEXITCODE)" }
Write-Host ("    intermediate: {0:N0} bytes" -f (Get-Item $primary).Length)

Write-Host '== [2/2] merge into single file ==' -ForegroundColor Cyan
# NOTE: deliberately NOT using /internalize. Internalizing every merged library type makes the
# output look like a packed/obfuscated blob, which is what packers and malware droppers do;
# keeping the types public makes the merge look like an ordinary multi-library application.
$out = Join-Path $binDir 'ssh_tunnel_ly.exe'
$mergeArgs = @(
    "/out:$out",
    '/ndebug', '/noRepackRes', '/skipconfig',
    "/lib:$fw",
    "/lib:$(Split-Path $sshnet -Parent)",
    "/lib:$(Split-Path $bclAsync -Parent)",
    "/lib:$(Split-Path $taskExt -Parent)",
    "/lib:$(Split-Path $unsafe -Parent)",
    $primary, $sshnet, $bclAsync, $taskExt, $unsafe
)
& $ilrepack @mergeArgs
if ($LASTEXITCODE -ne 0) { throw "ILRepack failed (exit code $LASTEXITCODE)" }

# The compiler emits the Win32 version resource as language neutral, so Explorer's Details tab
# shows "Language Neutral"; rewrite that field to Chinese (Simplified, PRC).
Write-Host '== [3/3] set version resource language ==' -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build\patch_lang.ps1') -Path $out
if ($LASTEXITCODE -ne 0) { throw "patch_lang failed (exit code $LASTEXITCODE)" }

$final = Get-Item $out
Write-Host ''
Write-Host ("BUILD OK: {0}" -f $final.FullName) -ForegroundColor Green
Write-Host ("size: {0:N0} bytes ({1:N2} MB)" -f $final.Length, ($final.Length / 1MB))
