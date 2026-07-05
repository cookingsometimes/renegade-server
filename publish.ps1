param(
    [switch]$Run,
    [int]$Port = 3420,
    [string]$XenoDir = ""
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $scriptDir 'RenegadeServer.csproj'
$outDir = Join-Path $scriptDir 'bin\publish'

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

Write-Host "Publishing RenegadeServer (single-file, self-contained)..."
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $outDir
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}

$exe = Join-Path $outDir 'RenegadeServer.exe'
if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe"
}

$size = (Get-Item $exe).Length / 1MB
Write-Host "Publish complete."
Write-Host "Executable: $exe ($([math]::Round($size, 1)) MB)"

if ($Run) {
    $runArgs = @("--port", "$Port")
    if ($XenoDir) {
        $runArgs += "--xeno-dir"
        $runArgs += $XenoDir
    }
    Write-Host "Starting RenegadeServer on port $Port..."
    Start-Process -FilePath $exe -ArgumentList $runArgs -WorkingDirectory $outDir
}
