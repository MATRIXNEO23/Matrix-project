# REA: anteprima predefinita, installazione/registrazione solo con -Apply.
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command npx -ErrorAction SilentlyContinue)) { throw 'npx non trovato; verifica Node gia installato.' }
$argsRea = @('-y', 'rea-agents@6.9.0', 'setup', '--client', 'claude_code', '--client', 'codex', '--skill=false')
if (-not $Apply) { $argsRea += '--dry-run' }
& npx @argsRea
exit $LASTEXITCODE
