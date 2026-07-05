param(
	[switch]$Run,
	[int]$Port = 3420
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptDir 'RenegadeServer.csproj'
$outDir = Join-Path $scriptDir 'bin\Release\net8.0\win-x64'

if (-not (Test-Path $project)) {
	throw "Project not found: $project"
}

Write-Host "Building..."
dotnet build $project -c Release -r win-x64
if ($LASTEXITCODE -ne 0) {
	throw "Build failed."
}

$exe = Join-Path $outDir 'RenegadeServer.exe'
if (-not (Test-Path $exe)) {
	throw "Executable not found: $exe"
}

Write-Host "Build complete: $exe"

if ($Run) {
	Write-Host "Starting on port $Port..."
	Start-Process -FilePath $exe -ArgumentList "--port $Port" -WorkingDirectory $outDir
}
