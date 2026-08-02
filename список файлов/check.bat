@echo off
chcp 65001 >nul 2>&1
setlocal
cd /d "%~dp0"

set "STARTPATH=%~dp0"

echo.
echo ================================================================
echo   NEW ERA · PROJECT REPORT
echo ================================================================
echo.
echo   Батник открыт из : %STARTPATH%
echo   Сейчас откроется окно выбора папки для отчёта.
echo.

:: ── Выбор папки (диалог стартует из папки батника) ──────────
set "TARGET="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms;$d=New-Object System.Windows.Forms.FolderBrowserDialog;$d.Description='Выбери папку для отчёта';$d.SelectedPath=$env:STARTPATH;$d.ShowNewFolderButton=$false;if($d.ShowDialog()-eq 'OK'){$d.SelectedPath}"`) do set "TARGET=%%P"

:: ── Если диалог закрыли — ввод пути вручную ─────────────────
if not defined TARGET (
    echo   [i] Окно закрыто. Укажи путь вручную.
    echo       Папка батника: %STARTPATH%
    set /p "TARGET=  Путь к папке для отчёта ^(Enter = папка батника^)> "
)
if not defined TARGET set "TARGET=%STARTPATH%"

:: убрать завершающий обратный слэш
if "%TARGET:~-1%"=="\" set "TARGET=%TARGET:~0,-1%"

set "ROOT=%TARGET%"
if not exist "%ROOT%\" (
    echo   [XX] Папка не найдена: %ROOT%
    pause
    exit /b 1
)
for %%I in ("%ROOT%") do set "NAME=%%~nxI"
set "OUT=%ROOT%\%NAME%_report.txt"

echo.
echo ================================================================
echo   FOLDER : %NAME%
echo   PATH   : %ROOT%
echo ================================================================
echo.
echo   [i] Формирую отчёт: %NAME%_report.txt
echo.

:: ─── Пути для PowerShell ────────────────────────────────────
set "NR_ROOT=%ROOT%"
set "NR_OUT=%OUT%"
set "NR_NAME=%NAME%"

:: ─── Отчёт (UTF-8 с BOM) ────────────────────────────────────
powershell -NoProfile -ExecutionPolicy Bypass -Command "$r=$env:NR_ROOT;$o=$env:NR_OUT;$nm=$env:NR_NAME;[Console]::OutputEncoding=[Text.Encoding]::UTF8;$m='.cs','.bat','.cmd','.ps1','.json','.xml','.csproj','.sln';$skip=@('bin','obj','.git','.vs','.vscode','.idea');$skipRe='\\(bin|obj|\.git|\.vscode|\.vs|\.idea)\\';function T($p,$pf){$e=Get-ChildItem -LiteralPath $p|Where-Object {($skip -notcontains $_.Name) -and ($_.FullName -ne $o)}|Sort-Object Name;$cnt=@($e).Count;for($i=0;$i-lt$cnt;$i++){$it=$e[$i];$l=$i-eq($cnt-1);if($l){$cn='\---'}else{$cn='+---'};$pf+$cn+$it.Name;if($it.PSIsContainer){if($l){$nx=$pf+'    '}else{$nx=$pf+'|   '};T $it.FullName $nx}}};$s=[Text.StringBuilder]::new();$L='='*64;[void]$s.AppendLine($L);[void]$s.AppendLine('  PROJECT REPORT : '+$nm);[void]$s.AppendLine('  GENERATED      : '+(Get-Date -Format 'dd.MM.yyyy HH:mm:ss'));[void]$s.AppendLine('  PATH           : '+$r);[void]$s.AppendLine($L);[void]$s.AppendLine('');[void]$s.AppendLine('==================== STRUCTURE ====================');[void]$s.AppendLine('');[void]$s.AppendLine($r);foreach($ln in @(T $r '')){[void]$s.AppendLine($ln)};[void]$s.AppendLine('');[void]$s.AppendLine('==================== SOURCE FILES ====================');$fc=0;$fs=Get-ChildItem -LiteralPath $r -Recurse -File|Where-Object {($m -contains $_.Extension.ToLower()) -and ($_.FullName -ne $o) -and ($_.FullName -notmatch $skipRe)};foreach($f in $fs){$fc++;$rel=$f.FullName.Substring($r.Length+1);[void]$s.AppendLine('');[void]$s.AppendLine('-'*48);[void]$s.AppendLine('FILE: '+$rel);[void]$s.AppendLine('SIZE: '+$f.Length+' bytes');[void]$s.AppendLine('-'*48);try{$txt=[IO.File]::ReadAllText($f.FullName,[Text.Encoding]::UTF8);[void]$s.AppendLine($txt.TrimEnd())}catch{[void]$s.AppendLine('  [!!] Ошибка чтения: '+$_.Exception.Message)};[void]$s.AppendLine('');Write-Host ('  [OK] '+$rel)};[void]$s.AppendLine('==================== SUMMARY ====================');[void]$s.AppendLine('  Source files : '+$fc);[void]$s.AppendLine('  Report file  : '+$nm+'_report.txt');[void]$s.AppendLine($L);[IO.File]::WriteAllText($o,$s.ToString(),[Text.UTF8Encoding]::new($true));Write-Host '';Write-Host ('  [OK] Обработано файлов: '+$fc);Write-Host ('  [OK] Отчёт сохранён   : '+$o)"

:: ─── Поиск Notepad++ ────────────────────────────────────────
set "NPP="
where notepad++ >nul 2>&1 && set "NPP=notepad++"
if not defined NPP for /f "tokens=2*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe" /ve 2^>nul') do set "NPP=%%B"
if not defined NPP for /f "tokens=2*" %%A in ('reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe" /ve 2^>nul') do set "NPP=%%B"
if not defined NPP if exist "%ProgramFiles%\Notepad++\notepad++.exe" set "NPP=%ProgramFiles%\Notepad++\notepad++.exe"
set "PF86=%ProgramFiles(x86)%"
if not defined NPP if exist "%PF86%\Notepad++\notepad++.exe" set "NPP=%PF86%\Notepad++\notepad++.exe"
if not defined NPP if exist "%LOCALAPPDATA%\Programs\Notepad++\notepad++.exe" set "NPP=%LOCALAPPDATA%\Programs\Notepad++\notepad++.exe"

:: ─── Открыть отчёт ──────────────────────────────────────────
echo.
set /p "OPEN=  Открыть отчёт? [y/N] "
if /i "%OPEN%"=="y" (
    if defined NPP (
        echo   [i] Открываю в Notepad++ ...
        start "" "%NPP%" "%OUT%"
    ) else (
        echo   [i] Notepad++ не найден — открываю в Блокноте.
        start "" notepad "%OUT%"
    )
)

endlocal
pause