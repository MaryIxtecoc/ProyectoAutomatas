@echo off
rem Inicia el servicio NLP en http://localhost:8000
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo Primero ejecuta instalar.bat
  pause
  exit /b 1
)
.venv\Scripts\python -m uvicorn app:app --host 127.0.0.1 --port 8000
