#!/usr/bin/env bash

ROOT="$(cd "$(dirname "$0")" && pwd)"

# Puerto único del backend. Debe coincidir con NUXT_PUBLIC_API_URL del
# frontend (frontend/.env y nuxt.config.ts usan http://localhost:5000/api).
BACKEND_PORT=5000

# Evita levantar una segunda copia (el puerto quedaría ocupado y fallaría).
if curl -s -o /dev/null "http://localhost:$BACKEND_PORT/api/Health" || curl -s -o /dev/null "http://localhost:3000"; then
    echo "AutomatasFinal ya está corriendo (puerto $BACKEND_PORT o 3000 ocupado)."
    echo "Cierra la otra ventana del iniciador (Ctrl+C) antes de volver a ejecutarlo."
    exit 1
fi

echo "======================================"
echo "      AutomatasFinal - Inicio"
echo "======================================"
echo ""

cleanup() {
    echo ""
    echo "Deteniendo AutomatasFinal..."

    kill "$NGROK_PID" 2>/dev/null
    kill "$API_PID" 2>/dev/null
    kill "$FRONTEND_PID" 2>/dev/null

    exit 0
}

trap cleanup INT TERM

if command -v ngrok >/dev/null 2>&1; then
    echo "Iniciando ngrok (puerto $BACKEND_PORT, el del backend)..."
    ngrok http "$BACKEND_PORT" --log=stdout > /tmp/automatasfinal-ngrok.log 2>&1 &
    NGROK_PID=$!
    echo "ngrok iniciado. PID: $NGROK_PID (log en /tmp/automatasfinal-ngrok.log)"
    echo ""
    sleep 2
else
    echo "ngrok no está instalado o no está en el PATH; se omite (instálalo si lo necesitas para el QR)."
    NGROK_PID=""
fi

echo "Iniciando API..."

cd "$ROOT/backend/AutomatasFinal.API" || exit 1

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://localhost:$BACKEND_PORT" \
    dotnet run --no-launch-profile &
API_PID=$!

echo "API iniciada. PID: $API_PID"
echo "Esperando a que la API responda (puede tardar si tiene que compilar)..."

# En vez de esperar un tiempo fijo, se espera a que /api/Health responda.
for _ in $(seq 1 120); do
    if curl -s -o /dev/null "http://localhost:$BACKEND_PORT/api/Health"; then
        echo "API lista."
        break
    fi
    if ! kill -0 "$API_PID" 2>/dev/null; then
        echo "La API se detuvo al arrancar. Revisa el error de arriba."
        exit 1
    fi
    sleep 1
done
echo ""

echo "Iniciando frontend..."

cd "$ROOT/frontend" || exit 1

npm run dev -- --host 0.0.0.0 &
FRONTEND_PID=$!

echo "Frontend iniciado. PID: $FRONTEND_PID"
echo "Esperando a que el frontend responda..."

for _ in $(seq 1 120); do
    if curl -s -o /dev/null "http://localhost:3000"; then
        break
    fi
    sleep 1
done

# IP de esta compu en la red local (para abrir el frontend desde otro equipo).
LAN_IP="$(ip route get 1.1.1.1 2>/dev/null | awk '{for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1)}')"

# URL pública de ngrok (la API local de ngrok la expone en :4040).
NGROK_URL=""
if [ -n "$NGROK_PID" ]; then
    for _ in $(seq 1 10); do
        NGROK_URL="$(curl -s http://localhost:4040/api/tunnels 2>/dev/null \
            | grep -o '"public_url":"https://[^"]*' | head -1 | cut -d'"' -f4)"
        [ -n "$NGROK_URL" ] && break
        sleep 1
    done
fi

echo ""
echo "=========================================================="
echo " FRONTEND"
echo "   Local:        http://localhost:3000"
[ -n "$LAN_IP" ] && echo "   Red local:    http://$LAN_IP:3000"
echo "   Login facial: http://localhost:3000/auth/facial"
echo "   Login QR:     http://localhost:3000/auth/qr"
echo ""
echo " BACKEND"
echo "   API:          http://localhost:$BACKEND_PORT/api"
echo "   Salud:        http://localhost:$BACKEND_PORT/api/Health"
echo "   Swagger:      http://localhost:$BACKEND_PORT/swagger"
if [ -n "$NGROK_URL" ]; then
    echo ""
    echo " NGROK (público, apunta al backend)"
    echo "   $NGROK_URL"
elif [ -n "$NGROK_PID" ]; then
    echo ""
    echo " NGROK: no se obtuvo la URL; revisa http://localhost:4040"
fi
echo "=========================================================="
echo ""
echo "Presiona Ctrl+C para detener todo."
echo ""

wait