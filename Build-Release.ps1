param(
    [string]$DotNetPath = 'dotnet',
    [string]$InnoCompilerPath = 'ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'LocalFileTools.csproj'

foreach ($arch in @('x64', 'x86')) {
    $rid = "win-$arch"
    $published = Join-Path $root ".build\$rid"
    $package = Join-Path $root $(if ($arch -eq 'x64') { 'package' } else { 'package-x86' })

    & $DotNetPath publish $project -c Release -r $rid --self-contained false -p:PublishSingleFile=true -o $published
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid" }

    New-Item -ItemType Directory -Path $package,(Join-Path $package 'Assets\tessdata'),(Join-Path $package $arch) -Force | Out-Null
    foreach ($name in @('LocalFileTools.exe', 'pdfium.dll', 'LICENSE', "Magick.Native-Q8-$arch.dll")) {
        Copy-Item -LiteralPath (Join-Path $published $name) -Destination $package -Force
    }
    foreach ($name in @('eng.traineddata', 'pdf.ttf')) {
        Copy-Item -LiteralPath (Join-Path $published "Assets\tessdata\$name") -Destination (Join-Path $package 'Assets\tessdata') -Force
    }
    foreach ($name in @('leptonica-1.82.0.dll', 'tesseract50.dll')) {
        Copy-Item -LiteralPath (Join-Path $published "$arch\$name") -Destination (Join-Path $package $arch) -Force
    }
}

& $InnoCompilerPath (Join-Path $root 'Installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
Write-Host "Built $(Join-Path $root 'LocalFileTools-Setup.exe')"
