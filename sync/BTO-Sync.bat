@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0bto-sync.ps1" %*
pause
