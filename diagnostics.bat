@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion
title New Era - Diagnostics
color 0E
set "ROOT=%~dp0"
set "OUT_DIR=%ROOT%program_from_the_cli"
set "REPORT=%ROOT%diagnostics_report.txt"
set "VER_FILE=%ROOT%version.txt"
set "CONFIG=%OUT_DIR%\qwen_config.txt"
set "HISTORY=%OUT_DIR%\chat_history.dat"
set "SSE_DUMP=%OUT_DIR%\last_sse.json"
set "CHAT_DUMP=%OUT_DIR%\last_chat.json"
set "CURSOR=%OUT_DIR%\qwen_cursor.txt"
echo.
echo   ================================================================
echo     NEW ERA · DIAGNOSTICS · %date% %time%
echo   ================================================================
echo.
echo   [i] Формирую отчёт: %REPORT%
echo.
(
echo ================================================================
echo   NEW ERA · DIAGNOSTICS REPORT
echo   DATE: %date% %time%
echo   ROOT: %ROOT%
echo ================================================================
echo.
) > "%REPORT%"
:: 1. СИСТЕМА
echo   [1/12] Система...
(
echo ==================== 1. SYSTEM ====================
echo.
echo OS: %OS%
echo Architecture: %PROCESSOR_ARCHITECTURE%
echo Processor: %PROCESSOR_IDENTIFIER%
echo Cores: %NUMBER_OF_PROCESSORS%
echo Computer: %COMPUTERNAME%
echo User: %USERNAME%
echo.
echo --- Windows Version ---
) >> "%REPORT%"
ver >> "%REPORT%" 2>&1
echo. >> "%REPORT%"
:: 2. .NET FRAMEWORK
echo   [2/12] .NET Framework...
(
echo ==================== 2. .NET FRAMEWORK ====================
echo.
echo --- .NET 4.x csc.exe ---
) >> "%REPORT%"
set "CSC_FOUND=NO"
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
echo   [OK] Framework64\v4.0.30319\csc.exe >> "%REPORT%"
set "CSC_FOUND=YES"
)
if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
echo   [OK] Framework\v4.0.30319\csc.exe >> "%REPORT%"
set "CSC_FOUND=YES"
)
if "%CSC_FOUND%"=="NO" (
echo   [!!] csc.exe НЕ найден >> "%REPORT%"
)
where dotnet >nul 2>&1
if !errorlevel! EQU 0 (
echo. >> "%REPORT%"
echo --- dotnet SDK --- >> "%REPORT%"
dotnet --version >> "%REPORT%" 2>&1
) else (
echo   [i] dotnet SDK не найден >> "%REPORT%"
)
echo. >> "%REPORT%"
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2>nul >> "%REPORT%"
echo. >> "%REPORT%"
:: 3. ФАЙЛЫ ПРОГРАММЫ
echo   [3/12] Файлы программы...
(
echo ==================== 3. PROGRAM FILES ====================
echo.
) >> "%REPORT%"
if exist "%OUT_DIR%\" (
echo --- program_from_the_cli --- >> "%REPORT%"
dir /a /o-d "%OUT_DIR%" >> "%REPORT%" 2>&1
) else (
echo   [!!] Папка НЕ существует: %OUT_DIR% >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%OUT_DIR%\main.exe" (
echo --- main.exe --- >> "%REPORT%"
for %%F in ("%OUT_DIR%\main.exe") do (
echo   Size: %%~zF bytes >> "%REPORT%"
echo   Date: %%~tF >> "%REPORT%"
)
) else (
echo   [!!] main.exe НЕ найден >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%OUT_DIR%\helper.exe" (
echo --- helper.exe --- >> "%REPORT%"
for %%F in ("%OUT_DIR%\helper.exe") do (
echo   Size: %%~zF bytes >> "%REPORT%"
echo   Date: %%~tF >> "%REPORT%"
)
) else (
echo   [!!] helper.exe НЕ найден >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%VER_FILE%" (
echo --- version.txt --- >> "%REPORT%"
type "%VER_FILE%" >> "%REPORT%" 2>&1
) else (
echo   [!!] version.txt НЕ найден >> "%REPORT%"
)
echo. >> "%REPORT%"
:: 4. КОНФИГУРАЦИЯ
echo   [4/12] Конфигурация...
(
echo ==================== 4. CONFIG ====================
echo.
) >> "%REPORT%"
if exist "%CONFIG%" (
echo   [OK] qwen_config.txt найден >> "%REPORT%"
echo. >> "%REPORT%"
echo   --- Проверка полей --- >> "%REPORT%"
findstr /i "^CHAT_ID=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
echo   [OK] CHAT_ID присутствует >> "%REPORT%"
) else (
echo   [!!] CHAT_ID ОТСУТСТВУЕТ >> "%REPORT%"
)
findstr /i "^TOKEN=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
echo   [OK] TOKEN присутствует ^(скрыт^) >> "%REPORT%"
) else (
echo   [!!] TOKEN ОТСУТСТВУЕТ >> "%REPORT%"
)
findstr /i "^API_URL=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^API_URL=" "%CONFIG%"') do (
echo   [i] API_URL=%%B >> "%REPORT%"
)
)
findstr /i "^MODEL=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^MODEL=" "%CONFIG%"') do (
echo   [i] MODEL=%%B >> "%REPORT%"
)
)
findstr /i "^AI2_TOKEN=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
echo   [OK] AI2_TOKEN присутствует ^(скрыт^) >> "%REPORT%"
) else (
echo   [i] AI2_TOKEN отсутствует >> "%REPORT%"
)
findstr /i "^AI2_LINK=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
echo   [OK] AI2_LINK присутствует >> "%REPORT%"
) else (
findstr /i "^AI2_CHAT_ID=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
echo   [OK] AI2_CHAT_ID присутствует >> "%REPORT%"
) else (
echo   [i] AI2_CHAT_ID / AI2_LINK отсутствует >> "%REPORT%"
)
)
findstr /i "^AI2_DISPATCHER=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^AI2_DISPATCHER=" "%CONFIG%"') do (
echo   [i] AI2_DISPATCHER=%%B >> "%REPORT%"
)
) else (
echo   [i] AI2_DISPATCHER не задан ^(OFF^) >> "%REPORT%"
)
findstr /i "^AI2_COMPRESS=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^AI2_COMPRESS=" "%CONFIG%"') do (
echo   [i] AI2_COMPRESS=%%B >> "%REPORT%"
)
)
findstr /i "^AI2_EXTRACT=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^AI2_EXTRACT=" "%CONFIG%"') do (
echo   [i] AI2_EXTRACT=%%B >> "%REPORT%"
)
)
findstr /i "^ARC_MODE=" "%CONFIG%" >nul 2>&1
if !errorlevel! EQU 0 (
for /f "tokens=1,* delims==" %%A in ('findstr /i "^ARC_MODE=" "%CONFIG%"') do (
echo   [i] ARC_MODE=%%B >> "%REPORT%"
)
)
echo. >> "%REPORT%"
echo   --- Все ключи ^(значения скрыты для TOKEN/COOKIE^) --- >> "%REPORT%"
findstr /i "^CHAT_ID= ^API_URL= ^MODEL= ^QWEN_VERSION= ^AI2_LINK= ^AI2_CHAT_ID= ^AI2_API_URL= ^AI2_MODEL= ^AI2_DISPATCHER= ^AI2_COMPRESS= ^AI2_EXTRACT= ^AI2_VALIDATE= ^PROJECT_PATH= ^ARC_MODE=" "%CONFIG%" >> "%REPORT%" 2>&1
findstr /i "^TOKEN=" "%CONFIG%" >nul 2>&1 && echo   TOKEN=******** >> "%REPORT%"
findstr /i "^AI2_TOKEN=" "%CONFIG%" >nul 2>&1 && echo   AI2_TOKEN=******** >> "%REPORT%"
findstr /i "^COOKIE=" "%CONFIG%" >nul 2>&1 && echo   COOKIE=******** >> "%REPORT%"
) else (
echo   [!!] qwen_config.txt НЕ найден: %CONFIG% >> "%REPORT%"
echo   [!!] Программа не сможет подключиться к API >> "%REPORT%"
)
echo. >> "%REPORT%"
:: 5. ПРОЦЕССЫ
echo   [5/12] Процессы...
(
echo ==================== 5. PROCESSES ====================
echo.
echo --- main.exe ---
) >> "%REPORT%"
tasklist /fi "imagename eq main.exe" /v /fo list 2>nul >> "%REPORT%"
(
echo.
echo --- helper.exe ---
) >> "%REPORT%"
tasklist /fi "imagename eq helper.exe" /v /fo list 2>nul >> "%REPORT%"
echo. >> "%REPORT%"
:: 6. СЕТЬ / API
echo   [6/12] Сеть и API...
(
echo ==================== 6. NETWORK / API ====================
echo.
echo --- DNS: chat.qwen.ai ---
) >> "%REPORT%"
nslookup chat.qwen.ai >> "%REPORT%" 2>&1
(
echo.
echo --- HTTPS connectivity ---
) >> "%REPORT%"
powershell -NoProfile -Command "try{$r=Invoke-WebRequest -Uri 'https://chat.qwen.ai' -Method HEAD -TimeoutSec 10 -UseBasicParsing;Write-Host ('  [OK] HTTP ' + $r.StatusCode)}catch{Write-Host ('  [!!] ' + $_.Exception.Message)}" >> "%REPORT%" 2>&1
(
echo.
echo --- Proxy ---
) >> "%REPORT%"
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable 2>nul >> "%REPORT%"
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer 2>nul >> "%REPORT%"
echo. >> "%REPORT%"
:: 7. ДАМПЫ И ЛОГИ
echo   [7/12] Дампы и логи...
(
echo ==================== 7. DUMPS AND LOGS ====================
echo.
) >> "%REPORT%"
if exist "%SSE_DUMP%" (
echo --- last_sse.json --- >> "%REPORT%"
for %%F in ("%SSE_DUMP%") do echo   Size: %%~zF bytes · Date: %%~tF >> "%REPORT%"
echo   --- Last 30 lines --- >> "%REPORT%"
powershell -NoProfile -Command "Get-Content -LiteralPath '%SSE_DUMP%' -Tail 30 -Encoding UTF8" >> "%REPORT%" 2>&1
) else (
echo   [i] last_sse.json не найден >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%CHAT_DUMP%" (
echo --- last_chat.json --- >> "%REPORT%"
for %%F in ("%CHAT_DUMP%") do echo   Size: %%~zF bytes · Date: %%~tF >> "%REPORT%"
) else (
echo   [i] last_chat.json не найден >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%HISTORY%" (
echo --- chat_history.dat --- >> "%REPORT%"
for %%F in ("%HISTORY%") do echo   Size: %%~zF bytes · Date: %%~tF >> "%REPORT%"
echo   --- Last 15 lines --- >> "%REPORT%"
powershell -NoProfile -Command "Get-Content -LiteralPath '%HISTORY%' -Tail 15 -Encoding UTF8" >> "%REPORT%" 2>&1
) else (
echo   [i] chat_history.dat не найден >> "%REPORT%"
)
echo. >> "%REPORT%"
if exist "%OUT_DIR%\plan.txt" (
echo --- plan.txt --- >> "%REPORT%"
type "%OUT_DIR%\plan.txt" >> "%REPORT%" 2>&1
) else (
echo   [i] plan.txt не найден >> "%REPORT%"
)
echo. >> "%REPORT%"
:: 8. EVENT LOG
echo   [8/12] Event Log...
(
echo ==================== 8. EVENT LOG ====================
echo.
echo --- Application errors (24h) ---
) >> "%REPORT%"
powershell -NoProfile -Command "try{Get-EventLog -LogName Application -EntryType Error -After (Get-Date).AddHours(-24) -ErrorAction Stop | Where-Object {$_.Message -match 'main|helper|\.NET|clr|csc'} | Select-Object -First 15 TimeGenerated,Source,Message | Format-List}catch{Write-Host '  [i] Нет ошибок или нет доступа'}" >> "%REPORT%" 2>&1
echo. >> "%REPORT%"
:: 9. ПРАВА ДОСТУПА
echo   [9/12] Права доступа...
(
echo ==================== 9. PERMISSIONS ====================
echo.
) >> "%REPORT%"
echo test > "%OUT_DIR%\__diag_test.tmp" 2>nul
if exist "%OUT_DIR%\__diag_test.tmp" (
echo   [OK] Запись в OUT_DIR разрешена >> "%REPORT%"
del /f /q "%OUT_DIR%\__diag_test.tmp" >nul 2>&1
) else (
echo   [!!] Запись в OUT_DIR ЗАПРЕЩЕНА или папка не существует >> "%REPORT%"
)
echo test > "%ROOT%__diag_test.tmp" 2>nul
if exist "%ROOT%__diag_test.tmp" (
echo   [OK] Запись в ROOT разрешена >> "%REPORT%"
del /f /q "%ROOT%__diag_test.tmp" >nul 2>&1
) else (
echo   [!!] Запись в ROOT ЗАПРЕЩЕНА >> "%REPORT%"
)
echo. >> "%REPORT%"
:: 10. ИСХОДНИКИ
echo   [10/12] Исходники...
(
echo ==================== 10. SOURCE FILES ====================
echo.
echo --- cli\main\*.cs ---
) >> "%REPORT%"
set "CS_COUNT=0"
for %%F in ("%ROOT%cli\main\*.cs") do (
set /a CS_COUNT+=1
echo   %%~nxF  ^(%%~zF bytes^) >> "%REPORT%"
)
echo. >> "%REPORT%"
echo   Total: !CS_COUNT! files >> "%REPORT%"
echo. >> "%REPORT%"
if exist "%ROOT%cli\helper\helper.cs" (
for %%F in ("%ROOT%cli\helper\helper.cs") do echo   [OK] helper.cs ^(%%~zF bytes^) >> "%REPORT%"
) else (
echo   [!!] helper.cs НЕ найден >> "%REPORT%"
)
echo. >> "%REPORT%"
echo --- Пустые .cs файлы --- >> "%REPORT%"
set "EMPTY_CS=0"
for %%F in ("%ROOT%cli\main\*.cs") do (
if %%~zF EQU 0 (
echo   [!!] ПУСТОЙ: %%~nxF >> "%REPORT%"
set /a EMPTY_CS+=1
)
)
if !EMPTY_CS! EQU 0 (
echo   [OK] Пустых .cs файлов нет >> "%REPORT%"
)
echo. >> "%REPORT%"
:: 11. PIPE / IPC
echo   [11/12] Pipe / IPC...
(
echo ==================== 11. PIPE / IPC ====================
echo.
echo --- Named Pipes ---
) >> "%REPORT%"
powershell -NoProfile -Command "try{[System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object {$_ -match 'NewEra'} | ForEach-Object { Write-Host ('  [OK] ' + $_) }}catch{Write-Host '  [i] Нет pipe NewEra'}" >> "%REPORT%" 2>&1
echo. >> "%REPORT%"
powershell -NoProfile -Command "Get-Process -Name main,helper -ErrorAction SilentlyContinue | Select-Object Name,Id,Handles,@{N='MemMB';E={[math]::Round($_.WorkingSet64/1MB,1)}},StartTime | Format-Table -AutoSize" >> "%REPORT%" 2>&1
echo. >> "%REPORT%"
:: 12. ДОПОЛНИТЕЛЬНО
echo   [12/12] Дополнительно...
(
echo ==================== 12. MISC ====================
echo.
echo --- Environment ---
) >> "%REPORT%"
echo   TEMP=%TEMP% >> "%REPORT%"
echo   APPDATA=%APPDATA% >> "%REPORT%"
echo. >> "%REPORT%"
powershell -NoProfile -Command "Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue | Where-Object {$_.Used -ne $null} | Select-Object Name,@{N='FreeGB';E={[math]::Round($_.Free/1GB,2)}},@{N='UsedGB';E={[math]::Round($_.Used/1GB,2)}} | Format-Table -AutoSize" >> "%REPORT%" 2>&1
echo. >> "%REPORT%"
if exist "%OUT_DIR%\" (
echo --- Структура OUT --- >> "%REPORT%"
dir /s /b "%OUT_DIR%" >> "%REPORT%" 2>&1
) else (
echo   [!!] OUT папка не существует >> "%REPORT%"
)
echo. >> "%REPORT%"
(
echo ================================================================
echo   END OF DIAGNOSTICS
echo   Generated: %date% %time%
echo ================================================================
) >> "%REPORT%"
:: ИТОГ
echo.
echo   ================================================================
echo     DIAGNOSTICS COMPLETE
echo   ================================================================
echo.
for %%F in ("%REPORT%") do echo   Отчёт: %%~zF bytes
echo.
echo   ── Быстрая сводка ──
echo.
if exist "%OUT_DIR%\main.exe" (echo   [OK] main.exe найден) else (echo   [!!] main.exe НЕ найден)
if exist "%OUT_DIR%\helper.exe" (echo   [OK] helper.exe найден) else (echo   [!!] helper.exe НЕ найден)
if exist "%CONFIG%" (echo   [OK] qwen_config.txt найден) else (echo   [!!] qwen_config.txt НЕ найден)
tasklist /fi "imagename eq main.exe" 2>nul | findstr /i "main.exe" >nul 2>&1
if !errorlevel! EQU 0 (echo   [OK] main.exe запущен) else (echo   [i] main.exe не запущен)
tasklist /fi "imagename eq helper.exe" 2>nul | findstr /i "helper.exe" >nul 2>&1
if !errorlevel! EQU 0 (echo   [OK] helper.exe запущен) else (echo   [i] helper.exe не запущен)
echo.
set /p "OPEN=  Открыть отчёт? [y/N] "
if /i "%OPEN%"=="y" (
set "NPP="
where notepad++ >nul 2>&1 && set "NPP=notepad++"
if not defined NPP if exist "%ProgramFiles%\Notepad++\notepad++.exe" set "NPP=%ProgramFiles%\Notepad++\notepad++.exe"
if defined NPP (
start "" "!NPP!" "%REPORT%"
) else (
start "" notepad "%REPORT%"
)
)
echo.
echo   [OK] Готово. Кидай diagnostics_report.txt ИИ для анализа.
echo.
pause
endlocal