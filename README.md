# Léxica — AutomatasFinal

Plataforma web de análisis léxico de documentos `.txt` en español, inglés, ruso, chino y árabe.
Proyecto final del curso **Lenguajes Formales y Autómatas** (2026).

## Funcionalidades

- **Registro** con fotografía, reconocimiento facial (recorte de rostro), filtros y stickers.
- **Credencial PDF con QR** enviada al registrarse; diseño personalizable por el usuario.
- **Inicio de sesión** con contraseña, con rostro (YuNet + SFace) o con el QR de la credencial.
- **Recuperación de contraseña** por correo o WhatsApp.
- **Análisis léxico**: total de palabras, más/menos frecuentes, pronombres, nombres propios,
  sustantivos y verbos en forma raíz, y otras clasificaciones. Reporte PDF por correo/WhatsApp.
- **Roles**: ADMIN (usuarios, parametrización, auditoría, solicitudes), SUPERVISOR (estadísticas)
  y ANALISTA (análisis y perfil).
- **Seguridad**: JWT con sesiones revocables, reCAPTCHA, contraseñas con hash, secreto del QR
  guardado como hash, RLS en la base de datos y bitácora de auditoría (quién, qué, cuándo, IP).

## Tecnologías

| Capa | Tecnología |
|---|---|
| Frontend | Nuxt 4 · Vue 3 · TypeScript |
| Backend | .NET 8 (ASP.NET Core Web API) · Entity Framework Core · Clean Architecture |
| Base de datos | PostgreSQL (Supabase) |
| Archivos | Supabase Storage (buckets privados) |
| Biometría | OpenCvSharp 4 · YuNet (detección) · SFace (reconocimiento) |
| PDF / QR | QuestPDF · QRCoder |
| Notificaciones | SMTP (MailKit) · Twilio WhatsApp |
| Contenedores | Docker · Docker Compose |
| Pruebas | xUnit |

## Estructura

```
backend/
  AutomatasFinal.Domain/          Entidades
  AutomatasFinal.Application/     DTOs y reglas compartidas
  AutomatasFinal.Infrastructure/  Servicios, EF Core, biometría, PDF, notificaciones
  AutomatasFinal.API/             Controladores y configuración
  AutomatasFinal.Tests/           Pruebas unitarias (xUnit)
frontend/                         Aplicación Nuxt (app/pages, app/components)
database/database.sql             Esquema completo, seguridad y datos iniciales
docker/                           Dockerfiles de backend y frontend
postman/collection.json           Colección de la API
```

## Requisitos

- .NET SDK 8
- Node.js 22 o superior
- Un proyecto de Supabase (PostgreSQL + Storage)
- Docker (opcional)

## Puesta en marcha

### 1. Base de datos

En Supabase → **SQL Editor**, ejecutar completo `database/database.sql`. Crea tablas, funciones,
seguridad (RLS), datos iniciales y los buckets privados de Storage. Los stickers del sistema
se suben a `stickers/global/`.

### 2. Variables de entorno

```bash
cp .env.example .env      # Windows: copy .env.example .env
```

Llenar `.env`. La cadena de conexión debe ser la de **Session pooler** de Supabase
(Project Settings → Database), que funciona por IPv4.

Cargar los valores en el backend:

```bash
./configurar-secretos.sh          # Linux / macOS
.\configurar-secretos.ps1         # Windows (PowerShell)
```

Frontend: crear `frontend/.env` con `NUXT_PUBLIC_API_URL=http://localhost:5000/api`.

### 3. Ejecutar

```bash
cd frontend && npm install && cd ..
./iniciar.sh                      # Linux
iniciar-todo.bat                  # Windows
```

| Servicio | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API | http://localhost:5000/api |
| Swagger (desarrollo) | http://localhost:5000/swagger |

### Con Docker

```bash
docker compose up --build
```

Frontend en http://localhost:3000 y API en http://localhost:8080/api (lee `.env`).

## Pruebas

```bash
cd backend
dotnet test
```

Cubren la política de contraseñas, el hash del QR, la validación del diseño de credencial,
el analizador léxico y el reconocimiento facial con fotografías de prueba.

## API

- Swagger en desarrollo: `/swagger`.
- Postman: importar `postman/collection.json`, ajustar `baseUrl` y ejecutar
  `Auth > POST /api/Auth/login` (el token se guarda automáticamente).

## Despliegue

- **Frontend**: Vercel (directorio raíz `frontend`, variable `NUXT_PUBLIC_API_URL`).
- **Backend**: cualquier servicio con Docker (por ejemplo Render), usando
  `docker/backend/Dockerfile` con contexto `backend` y puerto `8080`.
- Variables del backend en producción (formato `Seccion__Clave`):
  `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`,
  `Jwt__ExpirationMinutes`, `Supabase__Url`, `Supabase__SecretKey`, `App__FrontendBaseUrl`,
  `Cors__AllowedOrigins`, `ForwardedHeaders__TrustAllProxies=true`, `Smtp__*`, `Twilio__*`,
  `Recaptcha__SecretKey`.

## Seguridad

- Nunca subir `.env`, `frontend/.env` ni respaldos `.zip` (están en `.gitignore`).
- Todas las tablas tienen RLS; el backend es el único que accede a los datos.
- Los archivos se sirven a través del backend con JWT; los buckets son privados.
