# Register Unity 6000.5.1f1's official YAML merge tool in this repository only.
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tool = Join-Path $env:ProgramFiles 'Unity\Hub\Editor\6000.5.1f1\Editor\Data\Tools\UnityYAMLMerge.exe'
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
    throw "UnityYAMLMerge.exe not found at $tool. Edit the path to match your Unity 6000.5.1f1 installation."
}
$cmd = "'" + ($tool -replace '\\', '/') + "' merge -p " + '"$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config --local merge.tool unityyamlmerge
git config --local mergetool.unityyamlmerge.trustExitCode false
git config --local mergetool.unityyamlmerge.cmd placeholder
if ($LASTEXITCODE -ne 0) { throw 'Git configuration failed.' }
# Write the cmd as a quoted Git config value so embedded shell quotes survive PowerShell 5.1.
$configPath = Join-Path (Get-Location).Path '.git\config'
$lines = [System.IO.File]::ReadAllLines($configPath)
$inSection = $false
$found = $false
$value = '"' + $cmd.Replace('\', '\\').Replace('"', '\"') + '"'
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\[') { $inSection = ($lines[$i] -eq '[mergetool "unityyamlmerge"]') }
    if ($inSection -and $lines[$i] -match '^\s*cmd\s*=') {
        $lines[$i] = ('    cmd = ' + $value)
        $found = $true
        break
    }
}
if (-not $found) { throw 'Unity YAML merge config section not found.' }
[System.IO.File]::WriteAllLines($configPath, $lines, (New-Object System.Text.UTF8Encoding $false))
Write-Output "UnityYAMLMerge configured for $((Get-Location).Path)"
