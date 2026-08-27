@echo off
chcp 65001 >nul 2>&1
setlocal enabledelayedexpansion
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "OUT=%ROOT%\diagnostic.txt"
set "PS1=%TEMP%\leron_diag.ps1"
set "STOPFLAG=%TEMP%\leron_diag_stop.flag"

echo.
echo ================================================================
echo   LERON CLI · ДИАГНОСТИКА
echo ================================================================
echo.
echo   Запускаю приложение и пишу метрики каждые ~6 секунд.
echo   Отчёт: %OUT%
echo   Для остановки — в окне «СТОП» нажми любую кнопку.
echo.

if exist "%STOPFLAG%" del "%STOPFLAG%"

:: старт приложения — как в start.bat
start "LERON CLI" /d "%ROOT%\program\main" cmd /k "dotnet run"

:: скрипт метрик: CPU%% и RAM по процессам LeronCli / QwenCli / dotnet
> "%PS1%" echo $n = @('LeronCli','QwenCli','dotnet')
>> "%PS1%" echo $a = Get-Process -Name $n -ErrorAction SilentlyContinue
>> "%PS1%" echo if (-not $a) { $a = @() }
>> "%PS1%" echo Start-Sleep -Milliseconds 1000
>> "%PS1%" echo $b = Get-Process -Name $n -ErrorAction SilentlyContinue
>> "%PS1%" echo foreach ($x in $b) {
>> "%PS1%" echo   $o = $a.Where({ $_.Id -eq $x.Id })
>> "%PS1%" echo   $pct = 0
>> "%PS1%" echo   if ($o) { $pct = [math]::Round(($x.TotalProcessorTime.TotalMilliseconds - $o.TotalProcessorTime.TotalMilliseconds) / 10, 1) }
>> "%PS1%" echo   $ws = [math]::Round($x.WorkingSet64 / 1MB, 1)
>> "%PS1%" echo   Write-Output ("{0} {1} CPU={2}%% WS={3}MB" -f $x.Id, $x.Name, $pct, $ws)
>> "%PS1%" echo }
>> "%PS1%" echo $t = (Get-Counter '\Processor(_Total)\%% Processor Time' -ErrorAction SilentlyContinue).CounterSamples[0].CookedValue
>> "%PS1%" echo $m = (Get-Counter '\Memory\Available MBytes' -ErrorAction SilentlyContinue).CounterSamples[0].CookedValue
>> "%PS1%" echo Write-Output ("TOTAL CPU={0}%% AVAIL={1}MB" -f [math]::Round($t,1), [math]::Round($m,0))

:: шапка отчёта + инфо о системе
> "%OUT%" echo ================================================================
>> "%OUT%" echo   LERON CLI · DIAGNOSTIC LOG
>> "%OUT%" echo   STARTED: %date% %time%
>> "%OUT%" echo ================================================================
powershell -NoProfile -Command "(Get-CimInstance Win32_Processor ^| Select-Object -First 1).Name; [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1); [Environment]::OSVersion.Version" >> "%OUT%" 2>nul

:: окно остановки: любая кнопка создаёт флаг
start "LERON CLI · СТОП — нажми любую кнопку" cmd /c "pause >nul & type nul > ^"%STOPFLAG%^""

:loop
if exist "%STOPFLAG%" goto stop
>> "%OUT%" echo.
>> "%OUT%" echo --- [%date% %time%] ---
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" >> "%OUT%" 2>&1
echo   [%time%] образец записан
timeout /t 4 >nul
goto loop

:stop
if exist "%STOPFLAG%" del "%STOPFLAG%"
>> "%OUT%" echo.
>> "%OUT%" echo STOPPED: %date% %time%
echo.
echo   [OK] Стоп. Отчёт: %OUT%
start "" notepad "%OUT%"
endlocal
pause