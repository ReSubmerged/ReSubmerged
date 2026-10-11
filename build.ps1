Write-Host "Building project..." -ForegroundColor Cyan
dotnet build Submerged/Submerged.csproj -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}