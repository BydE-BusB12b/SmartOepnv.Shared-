@echo off
chcp 65001 >nul
cd /d "%~dp0"
title IBIS Wagenbus Utility - Build + Start
echo(
echo === Baue Release-Version ===
dotnet build "IbisWagenbusUtility.csproj" -c Release
if errorlevel 1 (
    echo Build fehlgeschlagen.
    pause
    exit /b 1
)
echo(
echo === Starte EXE ===
start "" "%~dp0bin\Release\net8.0-windows\IbisWagenbusUtility.exe"
echo IBIS Wagenbus Utility gestartet.
timeout /t 3 >nul
