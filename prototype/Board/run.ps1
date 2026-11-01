# PROTOTYPE: builds into C:\WAVEE\scratch so nothing lands in the engine checkout, then runs a copy.
# Arguments go to the app, for example: -- --variant B --open sso
$art = 'C:/WAVEE/scratch/art-thoughts'
$run = 'C:/WAVEE/scratch/thoughts/run'
dotnet build "$PSScriptRoot/Board.Prototype.csproj" -c Debug -nodeReuse:false --artifacts-path $art --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item "$art/bin/Board.Prototype/debug/*" $run -Recurse -Force
$exe = "$run/Board.Prototype.exe"
if ($args.Count -gt 0) { Start-Process $exe -ArgumentList $args -Wait } else { Start-Process $exe }
