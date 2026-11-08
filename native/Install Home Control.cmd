@echo off
rem Installs Home Control from this folder for the current user (no admin needed):
rem Start menu shortcut, and an entry in Settings > Apps for uninstalling.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
