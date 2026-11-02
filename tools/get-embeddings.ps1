<#
.SYNOPSIS
    Downloads what Thoughts needs for local embeddings: about 500 MB of model, and ONNX Runtime.

.DESCRIPTION
    Optional. Thoughts works without it, and it only changes anything above 300 notes.
    The files come from pypi.org and huggingface.co, and nothing about your notes is sent. They're not in this
    repo because of their size, and because the Qualcomm libraries (ARM64 only) have their own licence, which you accept
    by downloading them here.

.PARAMETER Destination
    Where to put them. The default is the place the app looks.
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Thoughts\embeddings')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$arm64 = $env:PROCESSOR_ARCHITECTURE -eq 'ARM64'
$platform = if ($arm64) { 'win_arm64' } else { 'win_amd64' }
$runtime = Join-Path $Destination 'runtime'
$model = Join-Path $Destination 'e5-small'
$work = Join-Path ([IO.Path]::GetTempPath()) ("thoughts-embeddings-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $runtime, $model, $work | Out-Null

function Get-Wheel($package, $version) {
    $info = Invoke-RestMethod "https://pypi.org/pypi/$package/$version/json"
    $wheel = $info.urls | Where-Object { $_.filename -like "*$platform*.whl" } | Select-Object -First 1
    if (-not $wheel) { throw "$package $version has no wheel for $platform." }
    Write-Host "Downloading $($wheel.filename)"
    $zip = Join-Path $work "$package.zip"
    Invoke-WebRequest $wheel.url -OutFile $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $wheel.digests.sha256) { throw "$($wheel.filename) didn't match its checksum." }
    $out = Join-Path $work $package
    Expand-Archive $zip $out
    Get-ChildItem $out -Recurse -Filter *.dll | Copy-Item -Destination $runtime -Force
}

try {
    Get-Wheel 'onnxruntime' '1.30.0'
    if ($arm64) { Get-Wheel 'onnxruntime-qnn' '2.6.0' }

    $base = 'https://huggingface.co/intfloat/multilingual-e5-small/resolve/main'
    foreach ($file in @(
            @{ Url = "$base/onnx/model.onnx"; Name = 'model.onnx' },
            @{ Url = "$base/sentencepiece.bpe.model"; Name = 'sentencepiece.bpe.model' })) {
        Write-Host "Downloading $($file.Name)"
        Invoke-WebRequest $file.Url -OutFile (Join-Path $model $file.Name)
    }
    Write-Host "Done. Installed in $Destination"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
