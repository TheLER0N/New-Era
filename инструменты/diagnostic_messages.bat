@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion

for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "GATEWAY_DIR=%ROOT%\program\sending and receiving"
set "LOG_FILE=%ROOT%\logs\messages_%date:~-4%%date:~3,2%%date:~0,2%.log"

if not exist "%ROOT%\logs" mkdir "%ROOT%\logs"

echo.
echo ================================================================
echo   LERON CLI · ДИАГНОСТИКА СООБЩЕНИЙ
echo ================================================================
echo.
echo   Корень     : %ROOT%
echo   Gateway    : %GATEWAY_DIR%
echo   Лог-файл   : %LOG_FILE%
echo.
echo   Гашу старый gateway на порту 51234...
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":51234" ^| findstr "LISTENING"') do (
    taskkill /PID %%P /F >nul 2>&1
)
timeout /t 2 >nul

echo   [i] Старт gateway с полным логированием...
echo.
echo   === НАЧАЛО ЛОГА [%date% %time%] === >> "%LOG_FILE%"

cd /d "%GATEWAY_DIR%"
dotnet run 2>&1 | powershell -NoProfile -Command "$input | ForEach-Object { $line = $_; $ts = Get-Date -Format 'HH:mm:ss'; Write-Host \"[$ts] $line\"; Add-Content -LiteralPath '%LOG_FILE%' -Value \"[$ts] $line\" -Encoding UTF8 }"

echo.
echo   === КОНЕЦ ЛОГА === >> "%LOG_FILE%"
echo   [OK] Лог сохранён: %LOG_FILE%
pause