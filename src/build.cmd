@echo off
rem Build dist\ZapretVN.exe. See build.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 pause
