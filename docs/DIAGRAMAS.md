# Documentación técnica — Diagramas (Persona 1)

Proyecto: **Léxica — AutomatasFinal** · Lenguajes Formales y Autómatas 2026

Todos los diagramas se generaron a partir del **código real** del repositorio:
`database/database.sql`, `backend/AutomatasFinal.Domain/Entities`,
`backend/AutomatasFinal.Infrastructure`, `backend/AutomatasFinal.API/Controllers`,
`nlp-service/` y `frontend/nuxt.config.ts`.

Cada diagrama se entrega en tres formatos:

| Formato | Uso |
|---|---|
| `.png` | Para el documento técnico y la presentación |
| `.svg` | Vectorial, se amplía sin perder calidad |
| `.mmd` / `.dot` | Código fuente (Mermaid / Graphviz) para editarlo o pasarlo a StarUML |

---

## 1. Diagrama entidad-relación (1.1.2.7) — `er/`

Base de datos **PostgreSQL (Supabase)**: 13 tablas, 3 tipos enumerados, 13 llaves foráneas.

| Tabla | Descripción | Relación principal |
|---|---|---|
| `roles` | Roles del sistema: ADMIN, SUPERVISOR, ANALISTA | 1 rol → N usuarios |
| `users` | Usuarios; contraseña solo como hash | Tabla central |
| `user_biometrics` | Descriptor facial (SFace) para login facial | 1 a 0..1 con usuario |
| `user_credentials` | Credencial PDF con QR (`qr_code_value` = SHA-256 del secreto) | 1 activa por usuario |
| `credential_designs` | Diseño personalizado de la credencial | N por usuario |
| `profile_change_requests` | Solicitudes de cambio de correo / fecha de nacimiento | `user_id` y `reviewed_by` → users |
| `password_reset_tokens` | Tokens de recuperación (solo hash, con IP) | N por usuario |
| `user_sessions` | Sesiones revocables (método: PASSWORD, FACE, QR, REGISTER) | `user_id` y `revoked_by` → users |
| `analysis_jobs` | Archivos .txt cargados y su estado | N por usuario |
| `analysis_results` | Resultado del análisis léxico (JSONB) | 1 a 0..1 con job (único) |
| `entity_dictionary` | Diccionario de entidades (PERSON, COUNTRY, CITY, ORGANIZATION…) | Independiente |
| `audit_logs` | Bitácora: quién, qué, cuándo, IP, user-agent | `user_id` y `target_user_id` → users |

**Tipos enumerados:** `analysis_language` (ES, EN, RU, ZH, AR) ·
`analysis_status` (PENDING, PROCESSING, COMPLETED, FAILED) ·
`notification_method` (email, whatsapp, both).

**Reglas de integridad:** correo y nickname únicos; un solo QR activo por usuario;
borrado en cascada de biometría, credenciales, sesiones, tokens y resultados;
`SET NULL` en auditoría y sesiones al borrar el usuario que revocó; todas las tablas con **RLS**.

**Storage (archivos):** buckets privados `profile-photos`, `credentials`, `stickers`,
`analysis-files`, `analysis-reports`.

---

## 2. Diagramas de clases (1.1.2.9) — `uml/clases/`

Arquitectura limpia (Clean Architecture) en 4 capas + servicio Python:

| Capa | Proyecto | Contenido |
|---|---|---|
| Dominio | `AutomatasFinal.Domain` | 11 entidades + 3 enumeraciones |
| Aplicación | `AutomatasFinal.Application` | DTOs y reglas (`PasswordPolicy`, `PhonePolicy`, `PasswordService`) |
| Infraestructura | `AutomatasFinal.Infrastructure` | Servicios, EF Core, biometría, PDF, notificaciones, NLP |
| API | `AutomatasFinal.API` | 13 controladores REST |
| NLP | `nlp-service` (Python) | FastAPI + Stanza + spaCy + autómata de reglas |

| Archivo | Muestra |
|---|---|
| `clases_dominio` | Entidades del dominio, atributos reales, multiplicidades y enumeraciones |
| `clases_usuarios` | Autenticación, usuarios, perfil y auditoría: controladores → servicios → interfaces |
| `clases_analisis` | Análisis léxico: `AnalysisService`, `TextAnalyzer`, `NlpServiceClient`, `NlpResultMerger` y el servicio Python con el **autómata finito** `RuleAutomaton` |

Patrones visibles: inyección de dependencias, interfaces para notificaciones
(`IEmailSender`, `IWhatsAppSender`, `INotificationService`, `IRecaptchaService`),
Singleton (`FaceRecognitionService`), Repository/Unit of Work (`AppDbContext`, EF Core),
Strategy/Fallback (NLP externo con respaldo en `TextAnalyzer`).

---

## 3. Casos de uso (1.1.2.11) — `uml/casos_uso/`

Actores: **Visitante**, **ANALISTA**, **SUPERVISOR** (hereda de Analista), **ADMIN**
(hereda de Supervisor) y sistemas externos: Correo (SMTP), WhatsApp (Twilio), Servicio NLP.

| CU | Caso de uso | Actor | Endpoint principal |
|---|---|---|---|
| CU01 | Registrarse (foto, filtros, stickers) | Visitante | `POST /api/Auth/register` |
| CU02 | Iniciar sesión con contraseña | Visitante | `POST /api/Auth/login` |
| CU03 | Iniciar sesión con rostro | Visitante | `POST /api/Auth/facial` |
| CU04 | Iniciar sesión con QR | Visitante | `POST /api/Auth/qr` |
| CU05 | Recuperar contraseña | Visitante | `POST /api/Auth/forgot-password`, `reset-password` |
| CU10 | Gestionar perfil | Analista | `GET /api/Profile/me`, `PUT /api/Profile` |
| CU11 | Cambiar contraseña | Analista | `PATCH /api/Profile/password` |
| CU12 | Cambiar foto | Analista | `POST /api/Profile/photo` |
| CU13 | Personalizar y regenerar credencial | Analista | `PUT /api/CredentialDesign`, `POST /api/Profile/credential/regenerate` |
| CU14 | Solicitar cambio de correo / fecha | Analista | `POST /api/ProfileChangeRequests` |
| CU20 | Cargar archivo .txt y elegir idioma | Analista | `POST /api/Analysis` |
| CU21 | Procesar análisis | Analista / Servicio NLP | `POST /api/Analysis/{id}/process` |
| CU22 | Ver resultados | Analista | `GET /api/Analysis/{id}` |
| CU23 | Descargar reporte PDF | Analista | `GET /api/Analysis/{id}/report` |
| CU24 | Enviar reporte (correo / WhatsApp) | Analista | `POST /api/Analysis/{id}/send-report` |
| CU25 | Ver historial, reintentar o eliminar | Analista | `GET /api/Analysis`, `POST …/retry`, `DELETE …` |
| CU30 | Ver estadísticas | Supervisor | `GET /api/Stats/overview` |
| CU31 | Consultar usuarios | Supervisor | `GET /api/Users` |
| CU40 | Gestionar usuarios | Admin | `POST/PUT/DELETE /api/Users` |
| CU41 | Cambiar rol | Admin | `PATCH /api/Users/{id}/role` |
| CU42 | Activar / desactivar usuario | Admin | `PATCH /api/Users/{id}/status` |
| CU43 | Aprobar / rechazar solicitudes | Admin | `POST /api/ProfileChangeRequests/{id}/approve`, `/reject` |
| CU44 | Consultar bitácora de auditoría | Admin | `GET /api/Audit/logs`, `/summary` |
| CU45 | Revocar sesiones | Admin | `POST /api/Audit/sessions/{id}/revoke` |
| CU46 | Parametrizar diccionario de entidades | Admin | `GET/POST/DELETE /api/EntityDictionary` |

Relaciones «include»: Registro e inicio de sesión incluyen *Validar reCAPTCHA*;
Registro incluye *Recibir credencial PDF con QR*; Cargar archivo incluye *Procesar análisis*;
Enviar reporte incluye *Descargar/generar PDF*; Desactivar usuario incluye *Revocar sesiones*.

---

## 4. Diagrama de arquitectura (1.1.2.13) — `architecture/`

### Entorno local (desarrollo / demostración)

| Componente | Tecnología | Dirección y puerto | Protocolo |
|---|---|---|---|
| Frontend | Nuxt 4 · Vue 3 · TypeScript | `127.0.0.1:3000` | HTTP |
| Backend API | .NET 8 · ASP.NET Core · EF Core | `127.0.0.1:5000` (`/api`, `/swagger`) | HTTP + JWT |
| Servicio NLP | Python 3.11 · FastAPI · Stanza · spaCy | `127.0.0.1:8000` | HTTP |
| ngrok (opcional) | Túnel hacia el backend | panel `127.0.0.1:4040` | HTTPS |
| PostgreSQL | Supabase (Session pooler) | `aws-0-us-east-2.pooler.supabase.com:5432` | TCP + SSL |
| Storage | Supabase Storage | `<proyecto>.supabase.co:443` | HTTPS |
| Correo | Gmail SMTP | `smtp.gmail.com:587` | SMTP + STARTTLS |
| WhatsApp | Twilio API | `api.twilio.com:443` | HTTPS |
| reCAPTCHA | Google | `www.google.com:443` | HTTPS |

### Producción (Vercel + Railway)

| Componente | Servidor | Dirección y puerto |
|---|---|---|
| Frontend | Vercel | `https://<app>.vercel.app` — 443 (SSL) |
| Backend | Railway (Docker, `aspnet:8.0`) | contenedor `:8080`, público `https://<backend>.up.railway.app:443` |
| Servicio NLP | Railway (Docker, `python:3.11-slim`) | red privada `nlp.railway.internal:8000`, **sin dominio público** |
| Base de datos y Storage | Supabase | igual que en local |

> Las IP públicas de Vercel, Railway y Supabase son dinámicas (detrás de balanceadores);
> por eso se identifican por **dominio y puerto**. Cuando se publique, reemplazar
> `<app>`, `<backend>` y `<proyecto>` por los dominios reales.

---

## Cómo editar o regenerar

- **Mermaid (`.mmd`)**: pegar el código en https://mermaid.live o en VS Code
  (extensión *Markdown Preview Mermaid Support*).
- **Graphviz (`.dot`)**: `dot -Tpng casos_uso.dot -o casos_uso.png`.
- **StarUML**: usar los PNG/SVG como referencia y redibujar si el catedrático
  pide el archivo `.mdj` nativo.
