# Despliegue en Railway

Proyecto de Railway con **2 servicios** (el frontend puede seguir en Vercel):

| Servicio | Carpeta (Root Directory) | Dockerfile | Puerto |
|---|---|---|---|
| `backend` | `backend` | `backend/Dockerfile` | 8080 |
| `nlp` | `nlp-service` | `nlp-service/Dockerfile` | 8000 |

## 1. Crear el proyecto

1. railway.com → **New Project → Deploy from GitHub repo** → elegir el repositorio.
2. Railway crea un servicio; renombrarlo a `backend`.
3. **+ New → GitHub Repo** (el mismo repositorio) para crear el servicio `nlp`.

## 2. Servicio `nlp` (Stanza + spaCy)

- **Settings → Root Directory:** `nlp-service`
- **Variables:**
  - `PORT` = `8000`
  - `NLP_MAX_LOADED_MODELS` = `2`
  - (opcional) `NLP_LANGUAGES` = `ES,EN,RU,ZH,AR`
- **No generar dominio público**: solo lo usa el backend por la red privada.
- La primera construcción tarda (descarga los modelos dentro de la imagen y
  ejecuta las pruebas del autómata; si fallan, no se publica).
- Memoria esperada: ~0,8–1,2 GB con 2 idiomas cargados.

## 3. Servicio `backend` (.NET 8)

- **Settings → Root Directory:** `backend`
- **Variables** (las mismas del `.env`, con formato `Seccion__Clave`):

| Variable | Valor |
|---|---|
| `PORT` | `8080` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | cadena del *Session pooler* de Supabase |
| `Jwt__Key` | clave nueva (mínimo 32 caracteres) |
| `Jwt__Issuer` | `AutomatasFinal.API` |
| `Jwt__Audience` | `AutomatasFinal.Client` |
| `Supabase__Url` / `Supabase__SecretKey` | de Supabase |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__FromEmail` | correo |
| `Recaptcha__SecretKey` | clave **real** de reCAPTCHA (no la de pruebas) |
| `App__FrontendBaseUrl` | URL del frontend (Vercel) |
| `Cors__AllowedOrigins` | URL del frontend (Vercel) |
| `ForwardedHeaders__TrustAllProxies` | `true` |
| **`NlpService__BaseUrl`** | **`http://${{nlp.RAILWAY_PRIVATE_DOMAIN}}:8000`** |
| `NlpService__TimeoutSeconds` | `90` |

- **Settings → Networking → Generate Domain** (este sí es público, con HTTPS).
- Health check: `/api/Health`.

`${{nlp.RAILWAY_PRIVATE_DOMAIN}}` es una referencia de Railway: se reemplaza
sola por la dirección interna del servicio `nlp`. El tráfico backend → nlp
no sale a internet.

## 4. Frontend (Vercel)

`NUXT_PUBLIC_API_URL` = `https://<dominio-del-backend>.up.railway.app/api`

## 5. Verificar

1. `https://<backend>/api/Health` responde OK.
2. Analizar `20_texto_largo.txt`: deben salir verbos como *volver*, *aprender*
   y personas *Carlos*, *María López*.
3. En los logs del servicio `nlp` debe aparecer `POST /analyze 200`.
4. Si el servicio `nlp` se detiene, el análisis sigue funcionando con el
   analizador propio del backend (respaldo automático).

## Costos y memoria

Railway cobra por uso (memoria y CPU por hora). Con ~1,5 GB en total entre
los dos servicios, revisar los precios actuales en railway.com/pricing.
Para gastar menos: `NLP_MAX_LOADED_MODELS=1` (más lento al cambiar de idioma)
o `NLP_LANGUAGES=ES,EN` (los demás idiomas usan el analizador propio).
