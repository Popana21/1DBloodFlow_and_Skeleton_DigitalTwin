@echo off
REM Thin wrapper so the workflow can still be started by double-clicking a .bat file.
powershell -ExecutionPolicy Bypass -File "%~dp0run_all.ps1"
pause
