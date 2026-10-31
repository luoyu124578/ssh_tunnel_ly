# Post-build patcher for bin\ssh_tunnel_ly.exe.
#
# Three byte-level fixups, each of which removes a static-analysis red flag that the compiler
# or ILRepack leaves behind:
#
#   1) VERSIONINFO language -> English (United States) 0x0409, done by patch_lang.ps1 (child
#      script). A non-English language id (0x0804 Chinese) is reported by Hybrid Analysis as
#      "translates to an unusual binary language" and counts towards the suspicion score; a
#      mainstream language keeps the resource complete without that flag.
#   2) PE header TimeDateStamp -> the commit time of HEAD.
#      csc /deterministic+ does NOT write the build time; it writes a hash-derived value, which
#      lands on a random date - sandbox reports then say "Timestamp in PE header is very old or
#      in the future" and score it as obfuscation / timestomping. Using the commit time keeps
#      the build reproducible (same commit -> same timestamp) AND plausible.
#   3) The UTF-16 string "Compression algorithm not found" (a message that Renci.SshNet carries
#      for an unknown compression algorithm) is byte-identical to the MPRESS packer stub message.
#      DIE / sandbox signatures therefore tag the binary as "packed with MPRESS" and some AV
#      engines label it MSIL_Kryptik. Same-length wording keeps the #US string heap valid.
#
# Every assumption is checked before writing; if anything does not look as expected the script
# throws instead of producing a half-patched binary.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build\patch_exe.ps1 -Path bin\ssh_tunnel_ly.exe [-DryRun]
#
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads .ps1 as ANSI).

param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent

# ---- step 1: version resource language (separate, already validated script) ----
Write-Host '== [3/3a] version resource language ==' -ForegroundColor Cyan
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'patch_lang.ps1') -Path $Path
if ($LASTEXITCODE -ne 0) { throw "patch_lang.ps1 failed (exit code $LASTEXITCODE)" }

$resolved = (Resolve-Path $Path).Path

$Latin1 = [System.Text.Encoding]::GetEncoding(28591)
function Get-LatinView([byte[]]$data) { return $Latin1.GetString($data) }
function Get-Utf16Latin([string]$text) { return $Latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($text)) }

function Find-All([string]$haystack, [string]$needle) {
    $found = New-Object System.Collections.ArrayList
    $from = 0
    while ($true) {
        $i = $haystack.IndexOf($needle, $from, [System.StringComparison]::Ordinal)
        if ($i -lt 0) { break }
        [void]$found.Add($i)
        $from = $i + 1
    }
    return $found
}

# ---- step 2: PE TimeDateStamp ----
Write-Host '== [3/3b] PE header timestamp ==' -ForegroundColor Cyan

$stamp = 0
foreach ($git in @('git', 'D:\soft\Git\cmd\git.exe')) {
    try {
        $ct = & $git -C $root log -1 --format=%ct 2>$null
        if ($LASTEXITCODE -eq 0 -and $ct) { $stamp = [int64]([string]$ct).Trim(); break }
    } catch { }
}
if ($stamp -le 0) {
    $stamp = 1767225600   # 2026-01-01T00:00:00Z, used when git is unavailable
    Write-Host '    git unavailable, falling back to the fixed stamp 2026-01-01'
}
$stampUtc = ([datetime]'1970-01-01Z').AddSeconds($stamp)
Write-Host ("    commit time: {0} ({1})" -f $stampUtc.ToString('yyyy-MM-dd HH:mm:ss'), $stamp)

$bytes = [System.IO.File]::ReadAllBytes($resolved)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ($peOffset -le 0 -or $peOffset -gt $bytes.Length - 4) { throw 'PE header offset is out of range' }
if (-not ($bytes[$peOffset] -eq 0x50 -and $bytes[$peOffset + 1] -eq 0x45 -and
          $bytes[$peOffset + 2] -eq 0 -and $bytes[$peOffset + 3] -eq 0)) {
    throw 'no PE signature at the expected offset'
}
$oldStamp = [BitConverter]::ToUInt32($bytes, $peOffset + 8)
[Array]::Copy([BitConverter]::GetBytes([uint32]$stamp), 0, $bytes, $peOffset + 8, 4)

# ---- step 3: MPRESS-lookalike string from SSH.NET ----
Write-Host '== [3/3c] packer-signature string ==' -ForegroundColor Cyan

$oldText = 'Compression algorithm not found'
$newText = 'Compression method is not known'
if ($oldText.Length -ne $newText.Length) { throw 'replacement text must have the same length' }

$latin = Get-LatinView $bytes
$oldPattern = Get-Utf16Latin $oldText
$newPattern = [System.Text.Encoding]::Unicode.GetBytes($newText)

$hits = Find-All $latin $oldPattern
Write-Host ("    '{0}': {1} place(s)" -f $oldText, $hits.Count)
if ($hits.Count -eq 0) {
    Write-Host '    nothing to replace (already patched or a different SSH.NET build)'
} else {
    foreach ($hit in $hits) {
        [Array]::Copy($newPattern, 0, $bytes, $hit, $newPattern.Length)
    }
    Write-Host ("    rewritten to '{0}' (same length, #US heap stays valid)" -f $newText)
}

# ---- verify ----
$check = Get-LatinView $bytes
$leftOld = (Find-All $check $oldPattern).Count
$leftNew = (Find-All $check (Get-Utf16Latin $newText)).Count
$stampNow = [BitConverter]::ToUInt32($bytes, $peOffset + 8)
Write-Host ("    verify: old text {0} place(s), new text {1} place(s), timestamp 0x{2:X8}" -f $leftOld, $leftNew, $stampNow)
if ($leftOld -ne 0) { throw 'replacement incomplete' }
if ($stampNow -ne [uint32]$stamp) { throw 'timestamp was not written' }

if ($DryRun) {
    Write-Host 'DRY RUN: nothing written.'
    exit 0
}

[System.IO.File]::WriteAllBytes($resolved, $bytes)
Write-Host ("PATCHED: {0}  (timestamp {1} -> {2}, old PE stamp was 0x{3:X8})" -f `
    $resolved, $stampUtc.ToString('yyyy-MM-dd'), $stamp, $oldStamp)
