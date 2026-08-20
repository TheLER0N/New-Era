@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion

cd /d "C:\Users\Пользователь\Desktop\cli"

echo.
echo ================================================================
echo   LERON CLI · PUSH TO GITHUB
echo ================================================================
echo.
echo   Репо: https://github.com/TheLER0N/New-Era
echo.
echo   [1] Полная перезапись (force push) — удалит старую историю
echo   [2] Обновление — обычный коммит поверх истории
echo   [3] Отмена
echo.

set /p "CHOICE=  Выбери действие [1/2/3] > "

if "%CHOICE%"=="3" goto :end
if "%CHOICE%"=="1" goto :full_rewrite
if "%CHOICE%"=="2" goto :normal_push
goto :end

:full_rewrite
echo.
echo   [!] Удаляю старую историю (.git)...
if exist ".git" rmdir /s /q ".git"
git init -b main
git remote add origin https://github.com/TheLER0N/New-Era.git 2>nul
goto :commit

:normal_push
echo.
echo   [i] Обновление существующей истории...
if not exist ".git" (
    git init -b main
    git remote add origin https://github.com/TheLER0N/New-Era.git 2>nul
)
goto :commit

:commit
:: Создаём .gitignore если его нет
if not exist ".gitignore" (
    (
        echo bin/
        echo obj/
        echo .vs/
        echo *.user
        echo logs/
        echo program/sending and receiving/logs/
        echo config.json
        echo *.exe
        echo Thumbs.db
    ) > ".gitignore"
)

git add -A

echo.
set /p "MSG=  Сообщение коммита [LERON CLI update] > "

if "!MSG!"=="" set "MSG=LERON CLI update"

git commit -m "!MSG!"

if "%CHOICE%"=="1" (
    echo.
    echo   [!] Force push в origin main...
    git push -f -u origin main
) else (
    echo.
    echo   [i] Push в origin main...
    git push -u origin main
)

:end
echo.
echo   Готово.
pause