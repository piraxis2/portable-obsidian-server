# 포터블 옵시디언 빌드 스크립트 (수정됨)

Write-Host "--- Portable Obsidian Web Bridge 빌드 시작 ---" -ForegroundColor Cyan

$outputDir = "PortableBuild"
if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
New-Item -ItemType Directory -Path $outputDir

# 1. Windows용 빌드
Write-Host "[1/2] Windows용 실행 파일 생성 중..." -ForegroundColor Yellow
dotnet publish PortableObsidian/PortableObsidian.csproj `
    -c Release `
    -r win-x64 `
    -o "$outputDir/Windows" `
    -p:PublishSingleFile=true `
    -p:SelfContained=true

# 2. Linux용 빌드
Write-Host "[2/2] Linux/스팀덱용 실행 파일 생성 중..." -ForegroundColor Yellow
dotnet publish PortableObsidian/PortableObsidian.csproj `
    -c Release `
    -r linux-x64 `
    -o "$outputDir/Linux" `
    -p:PublishSingleFile=true `
    -p:SelfContained=true

Write-Host "`n--- 빌드 완료! ---" -ForegroundColor Green
Write-Host "결과물 위치: $PSScriptRoot\$outputDir"
Write-Host "주의: 실행 시 해당 폴더의 'wwwroot' 폴더가 실행 파일과 함께 있어야 합니다."
