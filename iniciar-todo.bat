@echo off
setlocal

rem ============================================================
rem  Lanzador de AutomatasFinal (Windows)
rem  Abre 3 ventanas: ngrok, backend (dotnet run) y frontend (npm run dev)
rem  Ejecutar este .bat desde la RAIZ del proyecto (donde estan
rem  las carpetas backend/ y frontend/).
rem ============================================================

rem Puerto unico del backend. Debe coincidir con NUXT_PUBLIC_API_URL del
rem frontend (frontend\.env y nuxt.config.ts usan http://localhost:5000/api).
set BACKEND_PORT=5000

rem Evita levantar una segunda copia (el puerto quedaria ocupado y fallaria).
curl -s -o nul http://localhost:%BACKEND_PORT%/api/Health && goto yaCorriendo
curl -s -o nul http://localhost:3000 && goto yaCorriendo
goto iniciar
:yaCorriendo
echo AutomatasFinal ya esta corriendo (puerto %BACKEND_PORT% o 3000 ocupado).
echo Cierra las ventanas anteriores antes de volver a ejecutarlo.
pause
exit /b 1
:iniciar

echo Iniciando ngrok (puerto %BACKEND_PORT%)...
start "ngrok" cmd /k "ngrok http %BACKEND_PORT%"

timeout /t 2 /nobreak >nul

rem Servicio NLP (spaCy/Stanza). Opcional: si no esta instalado, el backend
rem usa su analizador propio. Para instalarlo: nlp-service\instalar.bat
if exist "nlp-service\.venv\Scripts\python.exe" (
  echo Iniciando servicio NLP en el puerto 8000...
  start "NLP - spaCy/Stanza" cmd /k "cd nlp-service && .venv\Scripts\python -m uvicorn app:app --host 127.0.0.1 --port 8000"
) else (
  echo Servicio NLP no instalado: se usara el analizador propio. Ver nlp-service\instalar.bat
)

echo Iniciando backend...
rem --no-launch-profile: si no, launchSettings.json impone el puerto 5234.
start "Backend - AutomatasFinal.API" cmd /k "cd backend\AutomatasFinal.API && set ASPNETCORE_ENVIRONMENT=Development&& set ASPNETCORE_URLS=http://localhost:%BACKEND_PORT%&& dotnet run --no-launch-profile"

rem Espera a que la API responda antes de abrir el frontend (max ~120 s).
echo Esperando a que la API responda (puede tardar si tiene que compilar)...
set /a TRIES=0
:waitapi
curl -s -o nul http://localhost:%BACKEND_PORT%/api/Health && goto apiready
set /a TRIES+=1
if %TRIES% geq 120 goto apiready
timeout /t 1 /nobreak >nul
goto waitapi
:apiready

echo Iniciando frontend...
start "Frontend - Nuxt" cmd /k "cd frontend && npm run dev"

rem Espera a que el frontend responda (max ~120 s).
set /a TRIES=0
:waitfront
curl -s -o nul http://localhost:3000 && goto frontready
set /a TRIES+=1
if %TRIES% geq 120 goto frontready
timeout /t 1 /nobreak >nul
goto waitfront
:frontready

rem IP local (para abrir desde otro equipo) y URL publica de ngrok.
set LAN_IP=
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "(Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null } | Select-Object -First 1).IPv4Address.IPAddress"`) do set LAN_IP=%%i
set NGROK_URL=
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "try { ((Invoke-RestMethod http://localhost:4040/api/tunnels).tunnels | Where-Object { $_.public_url -like 'https*' } | Select-Object -First 1).public_url } catch {}"`) do set NGROK_URL=%%i

echo.
echo ==========================================================
echo  FRONTEND
echo    Local:        http://localhost:3000
if defined LAN_IP echo    Red local:    http://%LAN_IP%:3000
echo    Login facial: http://localhost:3000/auth/facial
echo    Login QR:     http://localhost:3000/auth/qr
echo.
echo  BACKEND
echo    API:          http://localhost:%BACKEND_PORT%/api
echo    Salud:        http://localhost:%BACKEND_PORT%/api/Health
echo    Swagger:      http://localhost:%BACKEND_PORT%/swagger
echo.
if defined NGROK_URL (
  echo  NGROK ^(publico, apunta al backend^)
  echo    %NGROK_URL%
) else (
  echo  NGROK: no se obtuvo la URL; revisa http://localhost:4040
)
echo ==========================================================
echo.
echo Se abrieron 3 ventanas: ngrok, backend y frontend.
echo Cierra esas ventanas para detener todo.
echo.
echo Para que el QR de la credencial abra desde el celular, pon la URL
echo publica del FRONTEND en App:FrontendBaseUrl (user-secrets o variable
echo de entorno App__FrontendBaseUrl) antes de emitir la credencial.
echo.
pause
