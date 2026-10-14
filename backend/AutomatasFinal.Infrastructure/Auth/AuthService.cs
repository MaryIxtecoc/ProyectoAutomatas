    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using System.Text.Json;
    using AutomatasFinal.Application.Auth;
    using AutomatasFinal.Application.Security;
    using AutomatasFinal.Domain.Entities;
    using AutomatasFinal.Infrastructure.Audit;
    using AutomatasFinal.Infrastructure.Biometrics;
    using AutomatasFinal.Infrastructure.Credentials;
    using AutomatasFinal.Infrastructure.Data;
    using AutomatasFinal.Infrastructure.Notifications;
    using AutomatasFinal.Infrastructure.Sessions;
    using AutomatasFinal.Infrastructure.Storage;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;

    namespace AutomatasFinal.Infrastructure.Auth;

    public class AuthService
    {
        private const string PhotoBucket = "profile-photos";

        private readonly AppDbContext _db;
        private readonly PasswordService _passwordService;
        private readonly IConfiguration _configuration;
        private readonly FaceRecognitionService _faceRecognitionService;
        private readonly BiometricService _biometricService;
        private readonly CredentialService _credentialService;
        private readonly NotificationDispatcher _notificationDispatcher;
        private readonly SupabaseStorageService _storage;
        private readonly ILogger<AuthService> _logger;
        private readonly SessionService _sessionService;
        private readonly AuditService _audit;

        public AuthService(
            AppDbContext db,
            PasswordService passwordService,
            IConfiguration configuration,
            FaceRecognitionService faceRecognitionService,
            BiometricService biometricService,
            CredentialService credentialService,
            NotificationDispatcher notificationDispatcher,
            SupabaseStorageService storage,
            ILogger<AuthService> logger,
            SessionService sessionService,
            AuditService audit)
        {
            _db = db;
            _passwordService = passwordService;
            _configuration = configuration;
            _faceRecognitionService = faceRecognitionService;
            _biometricService = biometricService;
            _credentialService = credentialService;
            _notificationDispatcher = notificationDispatcher;
            _storage = storage;
            _logger = logger;
            _sessionService = sessionService;
            _audit = audit;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ArgumentException(
                    "Email y contraseña son obligatorios.");
            }

            PasswordPolicy.Validate(request.Password);

            var phone = PhonePolicy.Normalize(request.Phone);

            if (string.IsNullOrWhiteSpace(request.PhotoOriginalBase64))
            {
                throw new ArgumentException(
                    "La fotografía es obligatoria para completar el registro.");
            }

            var exists = await _db.Users
                .AnyAsync(x => x.Email == email);

            if (exists)
            {
                throw new InvalidOperationException(
                    "El correo electrónico ya está registrado.");
            }

            if (!Enum.TryParse<NotificationMethod>(
                    request.NotificationMethod.Trim(),
                    true,
                    out var notificationMethod))
            {
                throw new ArgumentException(
                    "Método de notificación inválido. Valores permitidos: EMAIL, WHATSAPP, BOTH.");
            }

            // Los usuarios que se autorregistran siempre quedan con el rol ANALISTA
            // (ver "Registro" en el documento del proyecto). Se busca por código en vez
            // de asumir un Id fijo, porque el orden de inserción de roles puede variar
            // entre entornos.
            var analistaRole = await _db.Roles
                .FirstOrDefaultAsync(x => x.Code == "ANALISTA");

            if (analistaRole is null)
            {
                throw new InvalidOperationException(
                    "No existe el rol ANALISTA en la base de datos. Revisa el seed de roles.");
            }

            // --- Fotografía + reconocimiento facial (recorte de rostro) ---
            var originalPhotoBytes = DecodeBase64Photo(
                request.PhotoOriginalBase64,
                "La fotografía enviada no es un Base64 válido.");

            var faceResult = _faceRecognitionService.AnalyzeFace(originalPhotoBytes);

            ValidateFaceResult(faceResult);

            var croppedOriginalBytes = _faceRecognitionService.CropToFace(
                originalPhotoBytes,
                faceResult);

            var modifiedPhotoBytes = string.IsNullOrWhiteSpace(request.PhotoModifiedBase64)
                ? croppedOriginalBytes
                : DecodeBase64Photo(
                    request.PhotoModifiedBase64!,
                    "La fotografía modificada enviada no es un Base64 válido.");

            var userId = Guid.NewGuid();

            var photoOriginalPath = $"{userId}/original.jpg";
            var photoModifiedPath = $"{userId}/modified.jpg";

            await Task.WhenAll(
                UploadPhotoAsync(photoOriginalPath, croppedOriginalBytes),
                UploadPhotoAsync(photoModifiedPath, modifiedPhotoBytes));

            var user = new User
            {
                Id = userId,
                Email = email,
                Phone = phone,
                BirthDate = request.BirthDate,
                Nickname = request.Nickname.Trim(),
                PasswordHash = _passwordService.HashPassword(request.Password),
                RoleId = analistaRole.Id,
                NotificationMethod = notificationMethod,
                PhotoOriginalPath = photoOriginalPath,
                PhotoModifiedPath = photoModifiedPath,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);

            var biometric = new UserBiometric
            {
                UserId = userId,
                FaceDescriptor = JsonSerializer.Serialize(faceResult.Descriptor),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.UserBiometrics.Add(biometric);

            // La cuenta se guarda primero. La emisión de la credencial (PDF+QR)
            // se intenta después y, si falla (por ejemplo el bucket
            // "credentials" no existe todavía, o algo de QuestPDF/QRCoder no
            // calzó con la versión instalada), NO tumba el registro: la cuenta
            // queda creada igual y el usuario puede pedir su credencial de
            // nuevo desde /api/profile/credential/regenerate una vez logueado.
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                AuditActions.Register,
                success: true,
                targetUserId: user.Id,
                details: new { role = analistaRole.Code, notificationMethod = notificationMethod.ToString() },
                actorUserId: user.Id);

            await TryIssueCredentialAsync(user, analistaRole.Name, modifiedPhotoBytes);

            return await BuildAuthResponseAsync(user, SessionService.MethodRegister);
        }

        /// <summary>
        /// Genera la credencial (PDF+QR), la guarda y encola su envío. Se usa
        /// tanto en el registro como en el endpoint de "regenerar credencial".
        /// No relanza la excepción: quien llama decide qué hacer si falla
        /// (en registro, se ignora para no tumbar la cuenta ya creada).
        /// </summary>
        public async Task<bool> TryIssueCredentialAsync(User user, string roleName, byte[] modifiedPhotoBytes)
        {
            try
            {
                var (credential, pdfBytes) = await _credentialService.IssueAsync(
                    user,
                    roleName,
                    modifiedPhotoBytes);

                // El PDF ya está en Storage (archivo propio). En una sola
                // transacción se desactiva la anterior y se guarda la nueva, en
                // ese orden (índice único: una sola activa por usuario). Si algo
                // falla, la credencial anterior queda activa e intacta.
                await using var transaction = await _db.Database.BeginTransactionAsync();

                var previousActive = await _db.UserCredentials
                    .Where(x => x.UserId == user.Id && x.IsActive)
                    .ToListAsync();

                foreach (var previous in previousActive)
                {
                    previous.IsActive = false;
                }

                await _db.SaveChangesAsync();

                _db.UserCredentials.Add(credential);

                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                await _audit.LogAsync(
                    previousActive.Count > 0
                        ? AuditActions.CredentialRegenerated
                        : AuditActions.CredentialCreated,
                    success: true,
                    targetUserId: user.Id,
                    details: new { credentialId = credential.Id, previousDeactivated = previousActive.Count });

                // En segundo plano: el registro tiene un límite de 30s y no debe
                // esperar a que SMTP/Twilio respondan para devolver el token.
                _notificationDispatcher.QueueCredential(
                    user.Id,
                    pdfBytes,
                    CredentialService.BucketName,
                    credential.PdfPath!);

                return true;
            }
            catch (Exception ex)
            {
                // Lo que quedó a medias en el contexto (credencial nueva, anteriores
                // marcadas inactivas) no debe guardarse en otro SaveChanges posterior.
                foreach (var entry in _db.ChangeTracker.Entries<UserCredential>().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                _logger.LogError(
                    ex,
                    "No fue posible emitir la credencial (PDF/QR) para el usuario {UserId}. " +
                    "Revisa que el bucket 'credentials' exista en Supabase Storage.",
                    user.Id);

                await _audit.LogAsync(
                    AuditActions.CredentialCreated,
                    success: false,
                    targetUserId: user.Id,
                    details: new { reason = "No fue posible generar la credencial (PDF/QR)." });

                return false;
            }
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await _db.Users
                .FirstOrDefaultAsync(x =>
                    x.Email == email &&
                    x.IsActive &&
                    x.DeletedAt == null);

            if (user is null)
            {
                await LogLoginFailedAsync(SessionService.MethodPassword, null, email, "Correo no registrado o cuenta inactiva.");
                return null;
            }

            var validPassword = _passwordService.VerifyPassword(
                request.Password,
                user.PasswordHash);

            if (!validPassword)
            {
                await LogLoginFailedAsync(SessionService.MethodPassword, user.Id, email, "Contraseña incorrecta.");
                return null;
            }

            await _audit.LogAsync(AuditActions.Login, true, user.Id, actorUserId: user.Id);

            return await BuildAuthResponseAsync(user, SessionService.MethodPassword);
        }

        /// <summary>
        /// Login facial: verificación 1 a 1 contra el rostro guardado del usuario indicado
        /// por correo. Es público (no requiere sesión previa).
        /// </summary>
        public async Task<AuthResponse?> LoginFacialAsync(FacialLoginRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await _db.Users
                .FirstOrDefaultAsync(x =>
                    x.Email == email &&
                    x.IsActive &&
                    x.DeletedAt == null);

            if (user is null)
            {
                await LogLoginFailedAsync(SessionService.MethodFace, null, email, "Correo no registrado o cuenta inactiva.");
                return null;
            }

            FaceVerificationResult verification;

            try
            {
                var photoBytes = DecodeBase64Photo(
                    request.PhotoBase64,
                    "La fotografía enviada no es un Base64 válido.");

                verification = await _biometricService.VerifyFaceAsync(user.Id, photoBytes);
            }
            catch (InvalidOperationException)
            {
                // El usuario no tiene un rostro registrado (o el guardado no es válido).
                await LogLoginFailedAsync(SessionService.MethodFace, user.Id, email, "El usuario no tiene un rostro registrado válido.");
                return null;
            }
            catch (ArgumentException ex)
            {
                // La foto enviada no contiene exactamente un rostro válido.
                // Los mensajes de validación facial son propios y no tienen datos sensibles.
                await LogLoginFailedAsync(SessionService.MethodFace, user.Id, email, ex.Message);
                return null;
            }

            // Solo similitud y umbral (nunca el descriptor): sirve para calibrar.
            _logger.LogInformation(
                "Login facial usuario {UserId}: similitud {Similarity:F3}, umbral {Threshold:F3}, verificado {Verified}",
                user.Id,
                verification.Similarity,
                verification.Threshold,
                verification.Verified);

            if (!verification.Verified)
            {
                await LogLoginFailedAsync(
                    SessionService.MethodFace,
                    user.Id,
                    email,
                    "El rostro no coincide.",
                    Math.Round(verification.Similarity, 3));
                return null;
            }

            await _audit.LogAsync(
                AuditActions.LoginFace,
                true,
                user.Id,
                details: new { similarity = Math.Round(verification.Similarity, 3) },
                actorUserId: user.Id);

            return await BuildAuthResponseAsync(user, SessionService.MethodFace);
        }

        /// <summary>
        /// Login por lectura del QR de la credencial del usuario. El valor del QR es un
        /// secreto opaco (no una URL); en BD solo existe su hash, así que se busca por hash.
        /// </summary>
        public async Task<AuthResponse?> LoginByQrAsync(QrLoginRequest request)
        {
            var qrSecret = request.QrCodeValue?.Trim();

            // Los secretos emitidos miden 43 (nuevos) o 64 (anteriores) caracteres;
            // cualquier cosa fuera de rango es un token malformado.
            if (string.IsNullOrWhiteSpace(qrSecret) ||
                qrSecret.Length > 128)
            {
                await LogLoginFailedAsync(SessionService.MethodQr, null, null, "Código QR malformado.");
                return null;
            }

            var qrHash = CredentialService.HashQrSecret(qrSecret);

            // Se busca sin filtrar por IsActive para poder auditar el uso de
            // un QR anterior (credencial ya reemplazada); igual se rechaza.
            var credential = await _db.UserCredentials
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.QrCodeValue == qrHash);

            if (credential is null)
            {
                await LogLoginFailedAsync(SessionService.MethodQr, null, null, "Código QR inexistente.");
                return null;
            }

            var user = credential.User;

            if (!credential.IsActive)
            {
                await LogLoginFailedAsync(SessionService.MethodQr, user.Id, user.Email, "Credencial desactivada (QR anterior).");
                return null;
            }

            if (!user.IsActive || user.DeletedAt != null)
            {
                await LogLoginFailedAsync(SessionService.MethodQr, user.Id, user.Email, "Usuario inactivo o eliminado.");
                return null;
            }

            await _audit.LogAsync(
                AuditActions.LoginQr,
                true,
                user.Id,
                details: new { credentialId = credential.Id },
                actorUserId: user.Id);

            return await BuildAuthResponseAsync(user, SessionService.MethodQr);
        }

        public async Task RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            string? requestedIp)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var user = await _db.Users
                .FirstOrDefaultAsync(x =>
                    x.Email == email &&
                    x.IsActive &&
                    x.DeletedAt == null);

            // No revelamos si el correo existe o no (evita enumeración de usuarios).
            // La bitácora sí lo registra: solo la ve ADMIN.
            if (user is null)
            {
                await _audit.LogAsync(
                    AuditActions.PasswordResetRequested,
                    false,
                    details: new { email, reason = "Correo no registrado o cuenta inactiva." });
                return;
            }

            var rawToken = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

            var resetToken = new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = _passwordService.HashPassword(rawToken),
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                RequestedIp = System.Net.IPAddress.TryParse(
                    requestedIp,
                    out var ip)
                    ? ip
                    : null,
                CreatedAt = DateTime.UtcNow
            };

            _db.PasswordResetTokens.Add(resetToken);

            await _db.SaveChangesAsync();

            // En segundo plano, por el mismo límite de tiempo que el registro.
            _notificationDispatcher.QueuePasswordReset(user.Id, rawToken);

            await _audit.LogAsync(
                AuditActions.PasswordResetRequested,
                true,
                user.Id,
                details: new { channel = user.NotificationMethod.ToString() },
                actorUserId: user.Id);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            PasswordPolicy.Validate(request.NewPassword, "La nueva contraseña");

            var candidates = await _db.PasswordResetTokens
                .Include(x => x.User)
                .Where(x =>
                    x.UsedAt == null &&
                    x.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            var match = candidates.FirstOrDefault(x =>
                _passwordService.VerifyPassword(
                    request.Token,
                    x.TokenHash));

            if (match is null)
            {
                await _audit.LogAsync(
                    AuditActions.PasswordResetFailed,
                    false,
                    details: new { reason = "Enlace inválido o expirado." });

                throw new ArgumentException(
                    "El enlace de restablecimiento es inválido o expiró.");
            }

            match.User.PasswordHash = _passwordService.HashPassword(
                request.NewPassword);

            match.User.UpdatedAt = DateTime.UtcNow;
            match.UsedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Si alguien tenía la cuenta abierta, al restablecer se le cierra.
            var closed = await _sessionService.RevokeAllForUserAsync(
                match.UserId,
                match.UserId,
                SessionService.ReasonPasswordReset);

            await _audit.LogAsync(
                AuditActions.PasswordResetCompleted,
                true,
                match.UserId,
                details: new { sessionsClosed = closed },
                actorUserId: match.UserId);
        }

        /// <summary>Cierra la sesión actual (la del JWT con el que se llamó).</summary>
        public async Task LogoutAsync(Guid userId, Guid? sessionId)
        {
            if (sessionId is not null)
            {
                await _sessionService.RevokeAsync(
                    sessionId.Value,
                    userId,
                    SessionService.ReasonLogout);
            }

            await _audit.LogAsync(
                AuditActions.Logout,
                true,
                userId,
                details: new { sessionId },
                actorUserId: userId);
        }

        private Task LogLoginFailedAsync(
            string method,
            Guid? userId,
            string? email,
            string reason,
            double? similarity = null)
        {
            // Nunca se registra la contraseña, la foto, el descriptor ni el valor del QR.
            return _audit.LogAsync(
                AuditActions.LoginFailed,
                false,
                userId,
                details: new { method, email, reason, similarity });
        }

        private async Task<AuthResponse> BuildAuthResponseAsync(User user, string loginMethod)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var expiresAt = DateTime.UtcNow.AddMinutes(
                _configuration.GetValue<int>("Jwt:ExpirationMinutes"));

            var session = await _sessionService.CreateAsync(
                user.Id,
                loginMethod,
                expiresAt);

            return new AuthResponse
            {
                Token = await GenerateTokenAsync(user, session.Id, expiresAt),
                UserId = user.Id,
                Email = user.Email,
                Nickname = user.Nickname,
                RoleId = user.RoleId
            };
        }

        private async Task UploadPhotoAsync(string path, byte[] bytes)
        {
            await using var stream = new MemoryStream(bytes);
            await _storage.UploadAsync(PhotoBucket, path, stream, "image/jpeg");
        }

        private static byte[] DecodeBase64Photo(string base64, string errorMessage)
        {
            // Se tolera que el frontend mande el prefijo "data:image/...;base64," por error.
            var commaIndex = base64.IndexOf(',');
            var pureBase64 = base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0
                ? base64[(commaIndex + 1)..]
                : base64;

            try
            {
                return Convert.FromBase64String(pureBase64);
            }
            catch (FormatException)
            {
                throw new ArgumentException(errorMessage);
            }
        }

        private static void ValidateFaceResult(FaceRecognitionResult result)
        {
            if (!result.FaceDetected)
            {
                throw new ArgumentException(
                    "No se detectó ningún rostro en la fotografía. Intenta con mejor iluminación.");
            }

            if (result.MultipleFaces || result.FaceCount != 1)
            {
                throw new ArgumentException(
                    "La fotografía debe contener exactamente un rostro.");
            }

            if (result.FaceTooSmall)
            {
                throw new ArgumentException(
                    "El rostro se ve demasiado pequeño en la fotografía. Acércate a la cámara.");
            }

            if (!FaceRecognitionService.IsValidDescriptor(result.Descriptor))
            {
                throw new ArgumentException(
                    "No fue posible generar el descriptor facial. Intenta de nuevo.");
            }
        }

        private async Task<string> GenerateTokenAsync(User user, Guid sessionId, DateTime expiresAt)
        {
            var key = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException(
                    "No se encontró Jwt:Key.");

            var issuer = _configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "No se encontró Jwt:Issuer.");

            var audience = _configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "No se encontró Jwt:Audience.");

            var roleCode = await _db.Roles
                .Where(x => x.Id == user.RoleId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(roleCode))
            {
                throw new InvalidOperationException(
                    "El usuario no tiene un rol válido.");
            }

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("nickname", user.Nickname),
                new Claim(ClaimTypes.Role, roleCode),
                // Id de la sesión (user_sessions): permite cerrarla antes de que el JWT expire.
                new Claim(SessionService.SessionClaim, sessionId.ToString())
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
