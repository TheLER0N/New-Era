@echo off
chcp 65001 >nul 2>&1
set "ROOT=%~dp0"
set "EXE=%ROOT%program\main\bin\Debug\net10.0-windows\LeronCli.exe"
echo.
echo ================================================================
echo   LERON CLI
echo ================================================================
echo.
echo   Гашу старый gateway на порту 51234 (если завис)...
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":51234" ^| findstr "LISTENING"') do (
    taskkill /PID %%P /F >nul 2>&1
)
timeout /t 1 >nul
echo   Собираю (после первого раза — быстро, инкрементально)...
dotnet build "%ROOT%program\main\MainApp.csproj" -v q --nologo
if not exist "%EXE%" (
    echo   [XX] Сборка не удалась — покажи мне вывод.
    pause
    exit /b 1
)
echo   Запускаю LERON CLI...
start "LERON CLI" /d "%ROOT%program\main" "%EXE%"
timeout /t 5