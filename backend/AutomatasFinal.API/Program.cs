using System.Text;
using AutomatasFinal.Application.Auth;
using AutomatasFinal.Application.Security;
using AutomatasFinal.Infrastructure.Analysis;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Auth;
using AutomatasFinal.Infrastructure.Biometrics;
using AutomatasFinal.Infrastructure.Credentials;
using AutomatasFinal.Infrastructure.Extensions;
using AutomatasFinal.Infrastructure.Notifications;
using AutomatasFinal.Infrastructure.Reports;
using AutomatasFinal.Infrastructure.Security;
using AutomatasFinal.Infrastructure.Sessions;
using AutomatasFinal.Infrastructure.Storage;
using AutomatasFinal.Infrastructure.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Singleton: el modelo SFace se carga una sola vez (acceso protegido con lock).
builder.Services.AddSingleton<FaceRecognitionService>();
builder.Services.AddScoped<BiometricService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<ProfileChangeRequestService>();
builder.Services.AddScoped<EntityDictionaryService>();
builder.Services.AddScoped<EntityDictionaryAdminService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<PdfReportService>();
builder.Services.AddScoped<TextAnalyzer>();
builder.Services.AddScoped<SupabaseStorageService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IWhatsAppSender, TwilioWhatsAppSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<NotificationDispatcher>();

builder.Services.AddScoped<CredentialPdfService>();
builder.Services.AddScoped<CredentialDesignService>();
builder.Services.AddScoped<CredentialService>();

// Auditoría y sesiones revocables.
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddScoped<SessionService>();

builder.Services.AddHttpClient<IRecaptchaService, RecaptchaService>();

// Servicio NLP externo (spaCy/Stanza). Si no responde, se usa TextAnalyzer.
builder.Services.AddHttpClient<NlpServiceClient>();

builder.Services.AddInfrastructure(builder.Configuration);

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "No se encontró la clave JWT 'Jwt:Key'.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],

            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Además de firma y expiración, la sesión del JWT debe seguir abierta
        // en user_sessions (un ADMIN pudo cerrarla, o el usuario hizo logout).
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessionId = SessionService.GetSessionId(context.Principal);
                var userId = RequestInfo.GetUserId(context.Principal);

                if (sessionId is null || userId is null)
                {
                    context.Fail("El token no tiene una sesión válida.");
                    return;
                }

                var sessions = context.HttpContext.RequestServices
                    .GetRequiredService<SessionService>();

                if (!await sessions.IsActiveAsync(sessionId.Value, userId.Value))
                {
                    context.Fail("La sesión fue cerrada o expiró.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// Orígenes permitidos en producción: Cors:AllowedOrigins (separados por coma)
// y el dominio de App:FrontendBaseUrl. Localhost y ngrok siguen permitidos.
var configuredOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Append(builder.Configuration["App:FrontendBaseUrl"] ?? string.Empty)
    .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
    .Select(x => new Uri(x).GetLeftPart(UriPartial.Authority))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                if (origin is "http://localhost:3000" or "http://127.0.0.1:3000" ||
                    configuredOrigins.Contains(origin))
                {
                    return true;
                }

                try
                {
                    var host = new Uri(origin).Host;

                    return host.EndsWith(
                               ".ngrok-free.app",
                               StringComparison.OrdinalIgnoreCase)
                           || host.EndsWith(
                               ".ngrok-free.dev",
                               StringComparison.OrdinalIgnoreCase)
                           || host.EndsWith(
                               ".ngrok.app",
                               StringComparison.OrdinalIgnoreCase)
                           || host.EndsWith(
                               ".ngrok.io",
                               StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Ingresa el JWT. Ejemplo: Bearer {token}"
        });

    options.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference =
                        new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                },
                Array.Empty<string>()
            }
        });
});

QuestPDF.Settings.License = LicenseType.Community;

// Fuentes del PDF para chino y árabe: se usan las fuentes del sistema
// (Windows: Microsoft YaHei, Segoe UI; Docker/Railway: Noto). Si una fuente
// de la lista no existe se omite en lugar de fallar, y si aun así falta algún
// carácter el PDF se genera igual (con un recuadro) en vez de dar error 500.
QuestPDF.Settings.UseEnvironmentFonts = true;
QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

// IP real del cliente detrás de ngrok / proxy (X-Forwarded-For). Solo se
// confía en el proxy local (loopback), que es donde corre el agente de ngrok.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // En hosting (Render, etc.) el proxy no es local: se confía en él
    // solo si se configura ForwardedHeaders:TrustAllProxies=true.
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

// Swagger expone el mapa completo de la API: solo en desarrollo.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();