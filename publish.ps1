$proj = "src/A2Meter/A2Meter.csproj"
$installerProj = "src/A2Updater/A2Updater.csproj"
$uninstallerProj = "src/A2Uninstaller/A2Uninstaller.csproj"
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { Write-Error "Version not found in csproj"; exit 1 }

$outDir = "E:\A2Viewer\A2Meter\publish\$version"
Write-Host "Publishing v$version -> $outDir" -ForegroundColor Cyan

dotnet publish $proj -c Release -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "A2Meter publish failed"
    exit 1
}

dotnet publish $installerProj -c Release -r win-x64 -o $outDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Installer publish failed"
    exit 1
}

dotnet publish $uninstallerProj -c Release -r win-x64 -o $outDir
if ($LASTEXITCODE -eq 0) {
    Write-Host "Done: $outDir" -ForegroundColor Green
} else {
    Write-Error "Uninstaller publish failed"
    exit 1
}
