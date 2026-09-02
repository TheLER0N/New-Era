@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion
set "ROOT=%~dp0"
set "PROJ=%ROOT%program\main\MainApp.csproj"
set "BUILD_DIR=%ROOT%GUI result"

echo.
echo ================================================================
echo   LERON · RELEASE TO GITHUB
echo ================================================================
echo.
echo   Репозиторий: https://github.com/TheLER0N/New-Era
echo.

:: ── Проверка окружения ──────────────────────────────────────────
where dotnet >nul 2>&1
if !errorlevel! neq 0 (
    echo   [XX] dotnet не найден. Установи .NET SDK.
    pause
    exit /b 1
)

if not exist "%PROJ%" (
    echo   [XX] MainApp.csproj не найден: %PROJ%
    pause
    exit /b 1
)

:: ── Ввод данных релиза ──────────────────────────────────────────
set /p "VER=   Версия (например 1.0.0): "
if "!VER!"=="" (
    echo   [XX] Версия не указана.
    pause
    exit /b 1
)

set /p "TITLE=   Название (Enter = LERON v!VER!): "
if "!TITLE!"=="" set "TITLE=LERON v!VER!"

set /p "DESC=    Описание (Enter = дефолт): "
if "!DESC!"=="" set "DESC=LERON v!VER! — GUI с AI-агентом, WebView2 и gateway."

echo.
echo ================================================================
echo   Версия   : !VER!
echo   Название : !TITLE!
echo   Проект   : %PROJ%
echo   Сборка   : %BUILD_DIR%
echo ================================================================
echo.
set /p "CONFIRM=   Продолжить? [Y/n]: "
if /i "!CONFIRM!"=="n" (
    echo   Отменено.
    pause
    exit /b 0
)

:: ── [1/4] Сборка Release ────────────────────────────────────────
echo.
echo   [1/4] Гашу старый gateway...
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":51234" ^| findstr "LISTENING"') do (
    taskkill /PID %%P /F >nul 2>&1
)
timeout /t 1 >nul

echo   [2/4] dotnet publish (Release)...
if exist "%BUILD_DIR%" (
    :: Не удаляем всю папку — бережём config.json и logs
    for %%F in ("%BUILD_DIR%\*.exe" "%BUILD_DIR%\*.dll" "%BUILD_DIR%\*.json" "%BUILD_DIR%\*.pdb") do (
        if /i not "%%~nxF"=="config.json" del "%%F" 2>nul
    )
    if exist "%BUILD_DIR%\runtimes" rd /s /q "%BUILD_DIR%\runtimes" 2>nul
)
dotnet publish "%PROJ%" -c Release -o "%BUILD_DIR%" --nologo -v q
if !errorlevel! neq 0 (
    echo.
    echo   [XX] Сборка не удалась — повтори dotnet publish вручную.
    pause
    exit /b 1
)
echo   [OK] Собрано в %BUILD_DIR%

:: ── [3/4] Создание ZIP ──────────────────────────────────────────
echo.
echo   [3/4] Создание архива...
set "ZIP_NAME=LERON-!VER!-win-x64.zip"
set "ZIP_PATH=%ROOT%release\!ZIP_NAME!"
if not exist "%ROOT%release" mkdir "%ROOT%release"
if exist "!ZIP_PATH!" del "!ZIP_PATH!"

powershell -NoProfile -Command "Compress-Archive -Path '%BUILD_DIR%\*' -DestinationPath '!ZIP_PATH!' -Force"
if !errorlevel! neq 0 (
    echo   [XX] Не удалось создать архив.
    pause
    exit /b 1
)

for %%A in ("!ZIP_PATH!") do set /a "ZIP_MB=%%~zA / 1048576"
echo   [OK] Архив: !ZIP_PATH! (~!ZIP_MB! MB)

:: ── [4/4] Открытие страницы релиза ──────────────────────────────
echo.
echo   [4/4] Открываю GitHub Releases...
echo.
echo ================================================================
echo   ГОТОВО!
echo ================================================================
echo.
echo   Что сделать в браузере:
echo.
echo   1. В поле "Tag version" введи:   !VER!
echo   2. В поле "Release title" введи: !TITLE!
echo   3. В поле "Describe" вставь:     !DESC!
echo   4. Нажми "attaching binaries"
echo   5. Выбери файл: !ZIP_PATH!
echo   6. Нажми "Publish release"
echo.
echo   Путь к архиву скопирован в буфер обмена —
echo   в поле выбора файла нажми Ctrl+V и Enter.
echo.

:: Копируем путь в буфер
echo|set /p="!ZIP_PATH!" | clip

start "" "https://github.com/TheLER0N/New-Era/releases/new?tag=!VER!&title=!TITLE!"

pause