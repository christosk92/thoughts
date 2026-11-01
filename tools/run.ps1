# Builds into C:\WAVEE\scratch, never into an engine checkout, and runs a copy so a rebuild is never locked.
# Arguments go to the app. For a check: tools\run.ps1 --vault C:\WAVEE\scratch\thoughts-vault --settings C:\WAVEE\scratch\thoughts-settings.json
$art = 'C:/WAVEE/scratch/art-thoughts-app'
$run = 'C:/WAVEE/scratch/thoughts-app/run'
dotnet build "$PSScriptRoot/../src/Thoughts/Thoughts.csproj" -c Debug -nodeReuse:false --artifacts-path $art --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
New-Item -ItemType Directory -Force $run | Out-Null
Copy-Item "$art/bin/Thoughts/debug/*" $run -Recurse -Force
$exe = "$run/Thoughts.exe"
if ($args.Count -gt 0) { Start-Process $exe -ArgumentList $args -Wait } else { Start-Process $exe }
