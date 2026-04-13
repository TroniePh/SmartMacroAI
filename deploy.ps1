# --- CẤU HÌNH ---
$repoName = "TroniePh/SmartMacroAI" # Thay bằng repo của bác
$projectFile = "SmartMacroAI.csproj"
$issFile = "installer\SmartMacroAI_Setup.iss"

# 1. Hỏi phiên bản mới
$currentVersion = Select-String -Path $projectFile -Pattern "<Version>(.*)</Version>" | ForEach-Object { $_.Matches.Value -replace "<Version>|</Version>", "" }
Write-Host "Phien ban hien tai: v$currentVersion" -ForegroundColor Cyan
$newVersion = Read-Host "Nhap phien ban moi (vi du: 1.1.2)"

if (-not $newVersion) { Write-Host "Huy bo."; exit }

# 2. Tu dong cap nhat Version trong file .csproj va .iss
Write-Host "Dang cap nhat version..." -ForegroundColor Yellow
(Get-Content $projectFile) -replace "<Version>.*</Version>", "<Version>$newVersion</Version>" | Set-Content $projectFile
(Get-Content $issFile) -replace "#define MyAppVersion .*", "#define MyAppVersion ""$newVersion""" | Set-Content $issFile

# 3. Build file EXE (Publish)
Write-Host "Dang Build file EXE..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./release_output

# 4. Build file Setup (Inno Setup)
Write-Host "Dang tao file cai dat (Setup)..." -ForegroundColor Yellow
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" $issFile

# 5. Day len GitHub
Write-Host "Dang day len GitHub va tao Release..." -ForegroundColor Green
git add .
git commit -m "Release v$newVersion"
git tag "v$newVersion"
git push origin main
git push origin "v$newVersion"

Write-Host "XONG! Bac cho 2-3 phut de GitHub Actions tu dong upload file len Release nhe." -ForegroundColor Green