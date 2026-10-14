@echo off
rem ============================================================
rem  Instala el servicio NLP (spaCy + Stanza) en Windows.
rem  Requiere Python 3.10, 3.11 o 3.12 (https://www.python.org).
rem  Se ejecuta UNA sola vez desde la carpeta nlp-service.
rem ============================================================
cd /d "%~dp0"

where py >nul 2>nul
if errorlevel 1 (
  echo No se encontro Python. Instala Python 3.11 desde https://www.python.org
  echo y marca la opcion "Add python.exe to PATH".
  pause
  exit /b 1
)

if exist ".venv\Scripts\python.exe" (
  echo El entorno virtual ya existe: solo se actualizan paquetes y modelos.
  echo Si el servicio NLP esta abierto, cierra su ventana antes de continuar.
) else (
  echo Creando entorno virtual...
  py -3.11 -m venv .venv || goto error
)

echo Instalando PyTorch (solo CPU)...
.venv\Scripts\python -m pip install --upgrade pip || goto error
.venv\Scripts\python -m pip install torch --index-url https://download.pytorch.org/whl/cpu || goto error

echo Instalando spaCy, Stanza y FastAPI...
.venv\Scripts\python -m pip install -r requirements.txt || goto error

echo Descargando modelos de idiomas (puede tardar varios minutos)...
.venv\Scripts\python descargar_modelos.py || goto error

echo.
echo Listo. El servicio se inicia solo con iniciar-todo.bat
pause
exit /b 0

:error
echo.
echo Ocurrio un error durante la instalacion. Revisa los mensajes de arriba.
pause
exit /b 1
