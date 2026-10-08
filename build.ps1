$ErrorActionPreference = 'Stop'
Write-Host 'HELLDRIVE // BUILD' -ForegroundColor Red
dotnet restore
dotnet build -c Release
Write-Host 'Build complete.' -ForegroundColor Green
