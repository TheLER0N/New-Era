@echo off
chcp 65001 >nul 2>&1
setlocal EnableDelayedExpansion
title New Era - Build and Run

:: --- Puti ---
set "ROOT=%~dp0"
set "SRC_MAIN_DIR=%ROOT%\cli\main"
set "SRC_HELPER=%ROOT%\cli\helper\helper.cs"
set "OUT=%ROOT%\program_from_the_cli"
set "VER_FILE=%ROOT%\version.txt"

:: --- Versiya iz fajla ---
set "VER=4.0"
if exist "%VER_FILE%" (
    set /p VER=<"%VER_FILE%"
)

:: --- Arhitektura ---
set "RID="
if /i "%PROCESSOR_ARCHITECTURE%"=="AMD64" set "RID=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="x86"   set "RID=win-x86"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM"   set "RID=win-arm"
if not defined RID set "RID=win-x64"

:: --- Shapka ---
color 0B
echo.
echo   ====================================================
echo     NEW ERA  -  BUILD and RUN  -  v%VER%
echo   ====================================================
echo.
echo   Arhitektura: %PROCESSOR_ARCHITECTURE%  (RID: %RID%)
echo.

:: --- Proverka ishodnikov ---
set "MAIN_CS_COUNT=0"
for %%F in ("%SRC_MAIN_DIR%\*.cs") do set /a MAIN_CS_COUNT+=1
if %MAIN_CS_COUNT%==0 (
    echo   [XX] Ne najdeny .cs fajly v: %SRC_MAIN_DIR%
    goto :fail
)
if not exist "%SRC_HELPER%" (
    echo   [XX] Ne najden: %SRC_HELPER%
    goto :fail
)
echo   [OK] Ishodniki: %MAIN_CS_COUNT% fajlov main + helper.cs

:: --- Vyhodnaya papka ---
if not exist "%OUT%" (
    mkdir "%OUT%"
    echo   [OK] Sozdana papka: program_from_the_cli
)

:: --- Poisk kompilyatora ---
set "CSC="
set "USE_DOTNET="

for %%V in (v4.0.30319 v4.0) do (
    if exist "%WINDIR%\Microsoft.NET\Framework64\%%V\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework64\%%V\csc.exe"
        goto :found_csc
    )
)
for %%V in (v4.0.30319 v4.0) do (
    if exist "%WINDIR%\Microsoft.NET\Framework\%%V\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework\%%V\csc.exe"
        goto :found_csc
    )
)

where dotnet >nul 2>&1
if %errorlevel%==0 (
    set "USE_DOTNET=1"
    goto :found_csc
)

echo   [XX] Kompilyator ne najden!
goto :fail

:found_csc
if defined USE_DOTNET (
    echo   [OK] Kompilyator: dotnet SDK
) else (
    echo   [OK] Kompilyator: csc.exe
)
echo.

:: --- Kompilyaciya MAIN ---
echo   --- main.exe ---
if defined USE_DOTNET (
    call :dotnet_build_dir "%SRC_MAIN_DIR%" "%OUT%\main.exe" "MainConsole"
) else (
    if exist "%OUT%\main.exe" del /f /q "%OUT%\main.exe" >nul 2>&1
    "%CSC%" /nologo /optimize+ /platform:anycpu /target:exe /r:System.Web.Extensions.dll /out:"%OUT%\main.exe" "%SRC_MAIN_DIR%\*.cs"
)
if errorlevel 1 (
    echo   [XX] Oshibka kompilyacii main
    goto :fail
)
echo   [OK] main.exe sobran.
echo.

:: --- Kompilyaciya HELPER ---
echo   --- helper.exe ---
if defined USE_DOTNET (
    call :dotnet_build "%SRC_HELPER%" "%OUT%\helper.exe" "QwenMessageFetcher"
) else (
    if exist "%OUT%\helper.exe" del /f /q "%OUT%\helper.exe" >nul 2>&1
    "%CSC%" /nologo /optimize+ /platform:anycpu /target:exe /r:System.Web.Extensions.dll /out:"%OUT%\helper.exe" "%SRC_HELPER%"
)
if errorlevel 1 (
    echo   [XX] Oshibka kompilyacii helper.cs
    goto :fail
)
echo   [OK] helper.exe sobran.
echo.

:: --- Kopiruem version.txt ---
if exist "%VER_FILE%" copy /y "%VER_FILE%" "%OUT%\version.txt" >nul 2>&1

:: --- Itog sborki ---
echo   ====================================================
echo     BUILD COMPLETE  -  v%VER%
echo   ====================================================
echo.
for %%F in ("%OUT%\main.exe")   do echo     main.exe    %%~zF bytes
for %%F in ("%OUT%\helper.exe") do echo     helper.exe  %%~zF bytes
echo.

:: --- Menyuu ---
echo   Vyberi dejstvie:
echo.
echo     [1] Zapustit klient
echo     [2] Ostanovit klient (taskkill)
echo     [3] Perezapustit (stop + start)
echo     [4] Vyjti
echo.
set "CHOICE="
set /p "CHOICE=  Vybor> "

if "%CHOICE%"=="2" goto :stop_client
if "%CHOICE%"=="3" goto :restart_client
if "%CHOICE%"=="4" goto :end
if not "%CHOICE%"=="1" goto :end

:: --- Zapusk ---
echo.
echo   [i] Zapusk klienta...
cd /d "%OUT%"
start "" "%OUT%\main.exe"
echo   [OK] Klient zapushhen.
echo.
goto :end

:: --- Ostanovka ---
:stop_client
echo.
echo   [i] Ostanavlivayu main.exe...
taskkill /f /im main.exe >nul 2>&1
if %errorlevel%==0 (
    echo   [OK] main.exe ostanovlen.
) else (
    echo   [i] main.exe ne zapushhen.
)
echo.
goto :end

:: --- Perezapusk ---
:restart_client
echo.
echo   [i] Ostanavlivayu...
taskkill /f /im main.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo   [i] Zapuskayu...
cd /d "%OUT%"
start "" "%OUT%\main.exe"
echo   [OK] Klient perezapushhen.
echo.
goto :end

:: --- Funkciya: sborka odnogo .cs cherez dotnet ---
:dotnet_build
set "CS_FILE=%~1"
set "EXE_OUT=%~2"
set "CLASS_NAME=%~3"
set "TMPDIR=%TEMP%\
ewera_build_%RANDOM%%RANDOM%"
set "BLOG=%TMPDIR%\build.log"
mkdir "%TMPDIR%"
copy "%CS_FILE%" "%TMPDIR%\Program.cs" >nul
call :write_csproj "%TMPDIR%" "%CLASS_NAME%"
pushd "%TMPDIR%"
dotnet publish -c Release -o "%TMPDIR%\out" --nologo -v q > "%BLOG%" 2>&1
set "DERR=%errorlevel%"
popd
if %DERR% neq 0 (
    echo   [XX] dotnet publish - oshibka:
    type "%BLOG%"
    rmdir /s /q "%TMPDIR%" >nul 2>&1
    exit /b 1
)
if not exist "%TMPDIR%\out\%CLASS_NAME%.exe" (
    echo   [XX] dotnet ne sozdal %CLASS_NAME%.exe
    rmdir /s /q "%TMPDIR%" >nul 2>&1
    exit /b 1
)
if exist "%EXE_OUT%" del /f /q "%EXE_OUT%" >nul 2>&1
copy /y "%TMPDIR%\out\%CLASS_NAME%.exe" "%EXE_OUT%" >nul
rmdir /s /q "%TMPDIR%" >nul 2>&1
goto :eof

:: --- Funkciya: sborka papki .cs cherez dotnet ---
:dotnet_build_dir
set "CS_DIR=%~1"
set "EXE_OUT=%~2"
set "CLASS_NAME=%~3"
set "TMPDIR=%TEMP%\
ewera_build_%RANDOM%%RANDOM%"
set "BLOG=%TMPDIR%\build.log"
mkdir "%TMPDIR%"
copy "%CS_DIR%\*.cs" "%TMPDIR%\" >nul
call :write_csproj "%TMPDIR%" "%CLASS_NAME%"
pushd "%TMPDIR%"
dotnet publish -c Release -o "%TMPDIR%\out" --nologo -v q > "%BLOG%" 2>&1
set "DERR=%errorlevel%"
popd
if %DERR% neq 0 (
    echo   [XX] dotnet publish - oshibka:
    type "%BLOG%"
    rmdir /s /q "%TMPDIR%" >nul 2>&1
    exit /b 1
)
if not exist "%TMPDIR%\out\%CLASS_NAME%.exe" (
    echo   [XX] dotnet ne sozdal %CLASS_NAME%.exe
    rmdir /s /q "%TMPDIR%" >nul 2>&1
    exit /b 1
)
if exist "%EXE_OUT%" del /f /q "%EXE_OUT%" >nul 2>&1
copy /y "%TMPDIR%\out\%CLASS_NAME%.exe" "%EXE_OUT%" >nul
rmdir /s /q "%TMPDIR%" >nul 2>&1
goto :eof

:: --- Funkciya: zapis .csproj ---
:write_csproj
set "CSPROJ_DIR=%~1"
set "CSPROJ_NAME=%~2"
(
echo ^<Project Sdk="Microsoft.NET.Sdk"^>
echo   ^<PropertyGroup^>
echo     ^<OutputType^>Exe^</OutputType^>
echo     ^<TargetFramework^>net48^</TargetFramework^>
echo     ^<AssemblyName^>%CSPROJ_NAME%^</AssemblyName^>
echo     ^<ImplicitUsings^>disable^</ImplicitUsings^>
echo     ^<Nullable^>disable^</Nullable^>
echo     ^<RuntimeIdentifier^>%RID%^</RuntimeIdentifier^>
echo     ^<SelfContained^>true^</SelfContained^>
echo     ^<PublishSingleFile^>false^</PublishSingleFile^>
echo   ^</PropertyGroup^>
echo   ^<ItemGroup^>
echo     ^<Reference Include="System.Web.Extensions" /^>
echo   ^</ItemGroup^>
echo ^</Project^>
) > "%CSPROJ_DIR%\build.csproj"
goto :eof

:fail
echo.
echo   [XX] SBORKA PRERVANA.
echo.

:end
endlocal
pause
