#!/usr/bin/env bash
# Copia los valores de .env a los user-secrets del backend (para correr con iniciar.sh).
# Uso: ./configurar-secretos.sh        (o --ver para solo listar qué se configuraría)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
ENV_FILE="$ROOT/.env"
API="$ROOT/backend/AutomatasFinal.API"

[ -f "$ENV_FILE" ] || { echo "Falta .env (copia .env.example y llénalo)."; exit 1; }

declare -A MAPA=(
  [DB_CONNECTION_STRING]="ConnectionStrings:DefaultConnection"
  [JWT_KEY]="Jwt:Key"
  [SUPABASE_URL]="Supabase:Url"
  [SUPABASE_SECRET_KEY]="Supabase:SecretKey"
  [FRONTEND_BASE_URL]="App:FrontendBaseUrl"
  [CORS_ALLOWED_ORIGINS]="Cors:AllowedOrigins"
  [SMTP_HOST]="Smtp:Host"
  [SMTP_PORT]="Smtp:Port"
  [SMTP_USER]="Smtp:User"
  [SMTP_PASSWORD]="Smtp:Password"
  [SMTP_FROM_EMAIL]="Smtp:FromEmail"
  [SMTP_FROM_NAME]="Smtp:FromName"
  [TWILIO_ACCOUNT_SID]="Twilio:AccountSid"
  [TWILIO_AUTH_TOKEN]="Twilio:AuthToken"
  [TWILIO_WHATSAPP_FROM]="Twilio:WhatsAppFrom"
  [RECAPTCHA_SECRET_KEY]="Recaptcha:SecretKey"
  [NLP_SERVICE_URL]="NlpService:BaseUrl"
)

while IFS='=' read -r key value; do
  [[ -z "$key" || "$key" == \#* ]] && continue
  dest="${MAPA[$key]:-}"
  [[ -z "$dest" || -z "$value" ]] && continue
  if [[ "${1:-}" == "--ver" ]]; then
    echo "$key -> $dest"
  else
    dotnet user-secrets set "$dest" "$value" --project "$API" >/dev/null
    echo "configurado $dest"
  fi
done < "$ENV_FILE"
