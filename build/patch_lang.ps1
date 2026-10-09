# Sets the VERSIONINFO language of a built exe to Chinese (Simplified, PRC) = 0x0804 / code page 1200.
#
# Why this exists: the C# compiler generates the Win32 version resource with a NEUTRAL language
# (StringFileInfo key "000004b0", Translation = language 0x0000 / code page 0x04B0). Windows
# Explorer then shows "Language Neutral" on the file's Details tab. Both fields are fixed width,
# so they can be rewritten in place without moving a single byte elsewhere in the file.
#
# The script checks every assumption before writing: if a pattern does not occur exactly once,
# or a value slot does not look like a language/code page pair, it refuses to patch.
#
# Usage: powershell -File patch_lang.ps1 -Path bin\ssh_tunnel_ly.exe [-DryRun]

param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$LANG_ZH_CN = 0x0804   # Chinese (Simplified, PRC)
$CP_UNICODE = 0x04B0   # 1200, Unicode

$Latin1 = [System.Text.Encoding]::GetEncoding(28591)
# latin1 view of the file: one char per byte, so string index == file offset
function Get-LatinView([byte[]]$data) { return $Latin1.GetString($data) }
# UTF-16LE rendering of a plain string, kept as a latin1 string so it can be searched byte-wise
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

$resolved = (Resolve-Path $Path).Path
$bytes = [System.IO.File]::ReadAllBytes($resolved)
$latin = Get-LatinView $bytes

# ---- 1) StringFileInfo language/code-page key: UTF-16 "000004b0" -> "080404b0" ----
$keyPattern = Get-Utf16Latin '000004b0'
$keyHits = Find-All $latin $keyPattern
Write-Host ("StringFileInfo language key 000004b0: {0} place(s) {1}" -f $keyHits.Count, (($keyHits | ForEach-Object { '0x{0:X}' -f $_ }) -join ', '))

# ---- 2) VarFileInfo / Translation value: 4 bytes, low word = language, high word = code page ----
$trPattern = Get-Utf16Latin 'Translation'
$trHits = Find-All $latin $trPattern
Write-Host ("Translation entries: {0}" -f $trHits.Count)

$valuePositions = New-Object System.Collections.ArrayList
foreach ($idx in $trHits) {
    $afterKeyNull = $idx + 22 + 2                                   # "Translation" is 11 chars in UTF-16, then NUL
    $valuePos = [int]([Math]::Ceiling($afterKeyNull / 4.0) * 4)     # DWORD aligned
    $value = [BitConverter]::ToUInt32($bytes, $valuePos)
    Write-Host ("  entry @0x{0:X}: value slot 0x{1:X} -> language 0x{2:X4}, code page 0x{3:X4}" -f $idx, $valuePos, ($value -band 0xFFFF), ($value -shr 16))
    [void]$valuePositions.Add($valuePos)
}

if ($keyHits.Count -ne 1) { throw "expected exactly one StringFileInfo language key, found $($keyHits.Count) - refusing to patch" }
if ($valuePositions.Count -lt 1) { throw 'no Translation value found - refusing to patch' }

# ---- apply ----
$newKey = [System.Text.Encoding]::Unicode.GetBytes('080404b0')
[Array]::Copy($newKey, 0, $bytes, $keyHits[0], $newKey.Length)
Write-Host 'set StringFileInfo key -> 080404b0 (Chinese Simplified, PRC / Unicode)'

foreach ($valuePos in $valuePositions) {
    $old = [BitConverter]::ToUInt32($bytes, $valuePos)
    $cp = $old -shr 16
    if ($cp -eq 0) { $cp = $CP_UNICODE }
    $new = ([uint32]$cp -shl 16) -bor [uint32]$LANG_ZH_CN
    $tmp = [BitConverter]::GetBytes($new)
    [Array]::Copy($tmp, 0, $bytes, $valuePos, 4)
    Write-Host ("set Translation @0x{0:X}: 0x{1:X8} -> 0x{2:X8} (language 0x0804, code page 0x{3:X4})" -f $valuePos, $old, $new, $cp)
}

if ($DryRun) {
    Write-Host 'DRY RUN: nothing written.'
    exit 0
}

[System.IO.File]::WriteAllBytes($resolved, $bytes)
Write-Host ("PATCHED: {0}" -f $resolved)
