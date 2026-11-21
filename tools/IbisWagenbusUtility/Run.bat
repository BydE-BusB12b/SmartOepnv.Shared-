@echo off
chcp 65001 >nul
cd /d "%~dp0"
title IBIS Wagenbus Utility - Schnellstart
echo(
echo === IBIS Wagenbus Utility starten ===

where dotnet >nul 2>&1
if errorlevel 1 (
    echo FEHLER: dotnet SDK nicht gefunden. Bitte .NET 8 SDK installieren.
    pause
    exit /b 1
)

set "EXE=bin\Debug\net8.0-windows\IbisWagenbusUtility.exe"

echo Beende ggf. laufende Instanz...
taskkill /IM "IbisWagenbusUtility.exe" /F >nul 2>&1

echo Baue Debug-Version (immer aktuell)...
dotnet build "IbisWagenbusUtility.csproj" -c Debug
if errorlevel 1 (
    echo Build fehlgeschlagen.
    pause
    exit /b 1
)

echo Starte: %EXE%
start "" "%~dp0%EXE%"
echo IBIS Wagenbus Utility wurde gestartet.
exit /b 0
