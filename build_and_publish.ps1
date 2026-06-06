$ErrorActionPreference = "Stop"

# ── paths ────────────────────────────────────────────────────────────
$peProj   = "src/PacketEngine/PacketEngine.csproj"
$appProj  = "src/A2Meter/A2Meter.csproj"
$installerProj = "src/A2Updater/A2Updater.csproj"
$uninstallerProj = "src/A2Uninstaller/A2Uninstaller.csproj"
$peDst    = "src/A2Meter/Native/PacketEngine.dll"

[xml]$csproj = Get-Content $appProj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$outDir  = "E:\A2Viewer\A2Meter\publish\$version"

# ── ensure vswhere is on PATH (NativeAOT linker needs it) ───────────
$vsInstallerPath = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if ($env:PATH -notlike "*$vsInstallerPath*") {
    $env:PATH = "$vsInstallerPath;$env:PATH"
}

# ── 1. PacketEngine NativeAOT ────────────────────────────────────────
Write-Host "[1/3] Publishing PacketEngine (NativeAOT)..." -ForegroundColor Cyan
dotnet publish $peProj -c Release -p:DisableUnsupportedError=true
if ($LASTEXITCODE -ne 0) { Write-Error "PacketEngine publish failed"; exit 1 }

$peSrc = "src/PacketEngine/bin/Release/net8.0/win-x64/publish/PacketEngine.dll"
Copy-Item $peSrc $peDst -Force
Write-Host "  -> Copied to $peDst" -ForegroundColor DarkGray

# ── 2. A2Meter build ────────────────────────────────────────────────
Write-Host "[2/3] Building A2Meter v$version..." -ForegroundColor Cyan
dotnet build $appProj -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# ── 3. A2Meter publish ──────────────────────────────────────────────
Write-Host "[3/5] Publishing A2Meter -> $outDir" -ForegroundColor Cyan
dotnet publish $appProj -c Release -r win-x64 --self-contained false -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed"; exit 1 }

Write-Host "[4/5] Publishing A2Meter installer -> $outDir" -ForegroundColor Cyan
dotnet publish $installerProj -c Release -r win-x64 -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "Installer publish failed"; exit 1 }

Write-Host "[5/5] Publishing A2Meter uninstaller -> $outDir" -ForegroundColor Cyan
dotnet publish $uninstallerProj -c Release -r win-x64 -o $outDir
if ($LASTEXITCODE -ne 0) { Write-Error "Uninstaller publish failed"; exit 1 }

Write-Host "Done: $outDir" -ForegroundColor Green
& "$outDir\A2Meter_Installer.exe"
