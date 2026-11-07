@echo off
cd /d "%~dp0"
python -c "import tinytuya" 2>nul || python -m pip install tinytuya
python led_app.py
pause
