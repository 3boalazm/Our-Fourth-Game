# Runs the game-rule tests inside PowerShell (no separate .exe, works where unsigned programs are blocked).
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $root "Assets\Scripts\Core\Arena.cs"), (Join-Path $root "Tests~\CoreTests.cs")
$failed = [CoreTests]::Run()
exit $failed
