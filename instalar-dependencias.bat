@echo off
setlocal EnableExtensions
rem ============================================================
rem  Instalador de dependencias de AutomatasFinal (Windows 10/11)
rem
rem  Instala lo que falte: .NET 8 SDK, Node.js LTS, Git, Python 3.11
rem  y (opcional) ngrok. Luego prepara el proyecto: claves del .env,
rem  frontend (npm install), backend (dotnet restore) y servicio NLP.
rem
rem  Uso: copiar el archivo .env en esta carpeta y dar doble clic.
rem  Se puede ejecutar varias veces: lo que ya esta instalado se omite.
rem ============================================================
cd /d "%~dp0"
title Instalador AutomatasFinal

echo.
echo ==========================================================
echo   INSTALADOR DE DEPENDENCIAS - AutomatasFinal (Lexica)
echo ==========================================================
echo.

where winget >nul 2>nul
if errorlevel 1 (
  echo [ERROR] No se encontro "winget" en este equipo.
  echo Instala "Instalador de aplicacion" desde Microsoft Store
  echo o instala manualmente: .NET 8 SDK, Node.js LTS, Git y Python 3.11.
  goto fin
)

set WG=winget install -e --accept-package-agreements --accept-source-agreements --silent

rem ---------------- 1. .NET 8 SDK ----------------
echo [1/5] .NET 8 SDK...
dotnet --list-sdks 2>nul | findstr /b "8." >nul
if errorlevel 1 (
  echo       Instalando .NET 8 SDK ^(puede pedir permiso de administrador^)...
  %WG% --id Microsoft.DotNet.SDK.8
) else (
  echo       Ya instalado.
)

rem ---------------- 2. Node.js ----------------
echo [2/5] Node.js...
where node >nul 2>nul
if errorlevel 1 (
  echo       Instalando Node.js LTS...
  %WG% --id OpenJS.NodeJS.LTS
) else (
  echo       Ya instalado.
)

rem ---------------- 3. Git ----------------
echo [3/5] Git...
where git >nul 2>nul
if errorlevel 1 (
  echo       Instalando Git...
  %WG% --id Git.Git
) else (
  echo       Ya instalado.
)

rem ---------------- 4. Python 3.11 ----------------
echo [4/5] Python 3.11 ^(servicio NLP^)...
py -3.11 --version >nul 2>nul
if errorlevel 1 (
  echo       Instalando Python 3.11...
  %WG% --id Python.Python.3.11 --scope user --override "/quiet InstallAllUsers=0 PrependPath=1 Include_launcher=1"
) else (
  echo       Ya instalado.
)

rem ---------------- 5. ngrok (opcional) ----------------
echo [5/5] ngrok ^(opcional, para abrir desde el celular^)...
where ngrok >nul 2>nul
if errorlevel 1 (
  choice /c SN /n /m "       Instalar ngrok? [S/N]: "
  if not errorlevel 2 %WG% --id Ngrok.Ngrok
) else (
  echo       Ya instalado. Si es version vieja, ejecuta: ngrok update
)

rem Recargar el PATH para usar lo recien instalado sin reiniciar la ventana.
for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "[Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')"`) do set "PATH=%%p"

echo.
echo ==========================================================
echo   PREPARANDO EL PROYECTO
echo ==========================================================
echo.

rem ---------------- Claves (.env) ----------------
if exist ".env" (
  echo [A] Cargando claves del .env en el backend...
  powershell -NoProfile -ExecutionPolicy Bypass -File ".\configurar-secretos.ps1"
) else (
  echo [A] [AVISO] No hay archivo .env en esta carpeta.
  echo     Pide el .env al grupo, copialo aqui y vuelve a ejecutar este instalador.
)

rem ---------------- Backend ----------------
echo.
echo [B] Descargando paquetes del backend ^(dotnet restore^)...
dotnet restore "backend\AutomatasFinal.API\AutomatasFinal.API.csproj"
if errorlevel 1 echo     [AVISO] Fallo dotnet restore. Cierra esta ventana, abre otra y vuelve a ejecutar.

rem ---------------- Frontend ----------------
echo.
echo [C] Instalando dependencias del frontend ^(npm install^)...
pushd frontend
call npm install
if errorlevel 1 echo     [AVISO] Fallo npm install. Si persiste, borra la carpeta frontend\node_modules y repite.
popd

rem ---------------- Servicio NLP ----------------
echo.
echo [D] Servicio NLP ^(spaCy/Stanza, analizador mejorado^).
echo     Descarga ~1 GB y tarda 10-15 minutos. Sin el, el sistema usa el analizador basico.
choice /c SN /n /m "    Instalarlo ahora? [S/N]: "
if not errorlevel 2 (
  call "nlp-service\instalar.bat"
  cd /d "%~dp0"
)

echo.
echo ==========================================================
echo   LISTO
echo   Para iniciar el sistema: doble clic en iniciar-todo.bat
echo   y abrir http://localhost:3000
echo.
echo   Si algo recien instalado no se reconoce, cierra esta ventana,
echo   abre otra y vuelve a ejecutar este instalador.
echo ==========================================================

:fin
echo.
pause
endlocal
