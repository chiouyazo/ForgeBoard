# Build and package ForgeBoard installer
# Requires: .NET 10 SDK, Inno Setup 6 (iscc.exe in PATH or default install location)

param(
  [string]$Configuration = "Release",
  [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "Building ForgeBoard $Version..." -ForegroundColor Cyan

Write-Host "Publishing API..." -ForegroundColor Yellow
$publishDir = Join-Path $repoRoot "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish "$repoRoot\src\ForgeBoard.Api" `
  -c $Configuration `
  -o $publishDir `
  --self-contained false

if ($LASTEXITCODE -ne 0) { throw "API publish failed" }

Write-Host "Publishing WASM frontend..." -ForegroundColor Yellow
$wasmPublishDir = Join-Path $repoRoot "publish-wasm"
if (Test-Path $wasmPublishDir) { Remove-Item $wasmPublishDir -Recurse -Force }

dotnet publish "$repoRoot\src\ForgeBoard" `
  -f net10.0-browserwasm `
  -c $Configuration `
  -o $wasmPublishDir

if ($LASTEXITCODE -ne 0) { throw "WASM publish failed" }

Write-Host "Bundling WASM frontend into API..." -ForegroundColor Yellow
$wasmWwwroot = Join-Path $wasmPublishDir "wwwroot"
$apiWwwroot = Join-Path $publishDir "wwwroot"

if (Test-Path $wasmWwwroot) {
  Copy-Item -Path "$wasmWwwroot\*" -Destination $apiWwwroot -Recurse -Force
  Write-Host "  WASM frontend bundled into $apiWwwroot"
}
else {
  Write-Host "  WARNING: WASM wwwroot not found at $wasmWwwroot" -ForegroundColor Yellow
}

Remove-Item $wasmPublishDir -Recurse -Force -ErrorAction SilentlyContinue

$issFile = Join-Path $PSScriptRoot "ForgeBoard.iss"
$issContent = Get-Content $issFile -Raw
$issContent = $issContent -replace '#define MyAppVersion ".*"', "#define MyAppVersion `"$Version`""
Set-Content $issFile $issContent -NoNewline

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
  $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
  if (Test-Path $isccPath) { $iscc = Get-Item $isccPath }
}

if (-not $iscc) {
  Write-Host "Inno Setup not found. Published files are in: $publishDir" -ForegroundColor Yellow
  Write-Host "Install Inno Setup 6 and run: iscc.exe ForgeBoard.iss" -ForegroundColor Yellow
  exit 0
}

Write-Host "Building installer..." -ForegroundColor Yellow
$artifactsDir = Join-Path $repoRoot "artifacts"
if (!(Test-Path $artifactsDir)) { New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null }

& $iscc.FullName $issFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

Write-Host ""
Write-Host "Installer built: $artifactsDir\ForgeBoard-Setup-$Version.exe" -ForegroundColor Green
