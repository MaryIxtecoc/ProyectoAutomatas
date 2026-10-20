# Copia los valores de .env a los user-secrets del backend (para correr con iniciar-todo.bat).
# Uso (PowerShell, en la raíz del proyecto): .\configurar-secretos.ps1   (o -Ver para solo listar)
param([switch]$Ver)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $root ".env"
$api = Join-Path $root "backend\AutomatasFinal.API"

if (-not (Test-Path $envFile)) { Write-Host "Falta .env (copia .env.example y llénalo)."; exit 1 }

$mapa = @{
  DB_CONNECTION_STRING = "ConnectionStrings:DefaultConnection"
  JWT_KEY              = "Jwt:Key"
  SUPABASE_URL         = "Supabase:Url"
  SUPABASE_SECRET_KEY  = "Supabase:SecretKey"
  FRONTEND_BASE_URL    = "App:FrontendBaseUrl"
  CORS_ALLOWED_ORIGINS = "Cors:AllowedOrigins"
  SMTP_HOST            = "Smtp:Host"
  SMTP_PORT            = "Smtp:Port"
  SMTP_USER            = "Smtp:User"
  SMTP_PASSWORD        = "Smtp:Password"
  SMTP_FROM_EMAIL      = "Smtp:FromEmail"
  SMTP_FROM_NAME       = "Smtp:FromName"
  TWILIO_ACCOUNT_SID   = "Twilio:AccountSid"
  TWILIO_AUTH_TOKEN    = "Twilio:AuthToken"
  TWILIO_WHATSAPP_FROM = "Twilio:WhatsAppFrom"
  RECAPTCHA_SECRET_KEY = "Recaptcha:SecretKey"
  NLP_SERVICE_URL      = "NlpService:BaseUrl"
}

foreach ($line in Get-Content $envFile -Encoding UTF8) {
  if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
  $key, $value = $line -split '=', 2
  $key = $key.Trim(); $value = $value.Trim()
  if (-not $mapa.ContainsKey($key) -or [string]::IsNullOrEmpty($value)) { continue }
  if ($Ver) { Write-Host "$key -> $($mapa[$key])" }
  else {
    dotnet user-secrets set $mapa[$key] $value --project $api | Out-Null
    Write-Host "configurado $($mapa[$key])"
  }
}
