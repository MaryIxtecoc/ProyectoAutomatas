namespace AutomatasFinal.Infrastructure.Audit;

/// <summary>
/// Catálogo de acciones auditadas. audit_logs.action es texto libre (sin ENUM
/// en BD), así que este catálogo es la única fuente de verdad: lo usan los
/// servicios para escribir y el panel de auditoría para filtrar/mostrar.
/// </summary>
public static class AuditActions
{
    // --- Sesiones ---
    public const string Login = "LOGIN";
    public const string LoginFace = "LOGIN_FACE";
    public const string LoginQr = "LOGIN_QR";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string Logout = "LOGOUT";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string Register = "REGISTER";

    // --- Gestión de usuarios (ADMIN) ---
    public const string UserCreated = "USER_CREATED";
    public const string UserUpdated = "USER_UPDATED";
    public const string RoleChanged = "ROLE_CHANGED";
    public const string UserDisabled = "USER_DISABLED";
    public const string UserEnabled = "USER_ENABLED";
    public const string UserDeleted = "USER_DELETED";
    public const string ProfileChangeRequested = "PROFILE_CHANGE_REQUEST";
    public const string ProfileChangeApproved = "PROFILE_CHANGE_APPROVED";
    public const string ProfileChangeRejected = "PROFILE_CHANGE_REJECTED";

    // --- Perfil ---
    public const string ProfileUpdated = "PROFILE_UPDATED";
    public const string PhotoUpdated = "PHOTO_UPDATED";
    public const string CredentialDesignUpdated = "CREDENTIAL_DESIGN_UPDATED";
    public const string CredentialDesignReset = "CREDENTIAL_DESIGN_RESET";
    public const string StickerUploaded = "STICKER_UPLOADED";
    public const string StickerDeleted = "STICKER_DELETED";

    // --- Seguridad ---
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordChangeFailed = "PASSWORD_CHANGE_FAILED";
    public const string PasswordResetRequested = "PASSWORD_RESET_REQUESTED";
    public const string PasswordResetCompleted = "PASSWORD_RESET_COMPLETED";
    public const string PasswordResetFailed = "PASSWORD_RESET_FAILED";
    public const string CredentialCreated = "CREDENTIAL_CREATED";
    public const string CredentialRegenerated = "CREDENTIAL_REGENERATED";
    public const string BiometricRegistered = "BIOMETRIC_REGISTERED";
    public const string BiometricDeleted = "BIOMETRIC_DELETED";

    // --- Análisis ---
    public const string AnalysisCreated = "ANALYSIS_CREATED";
    public const string AnalysisDeleted = "ANALYSIS_DELETED";
    public const string ReportSent = "REPORT_SENT";

    // --- Sistema (parametrización) ---
    public const string DictionaryEntryCreated = "DICTIONARY_ENTRY_CREATED";
    public const string DictionaryEntryDeleted = "DICTIONARY_ENTRY_DELETED";

    public const string CategorySessions = "SESIONES";
    public const string CategoryUsers = "USUARIOS";
    public const string CategoryProfile = "PERFIL";
    public const string CategorySecurity = "SEGURIDAD";
    public const string CategoryAnalysis = "ANALISIS";
    public const string CategorySystem = "SISTEMA";

    public record ActionInfo(string Action, string Category, string Label);

    public static readonly IReadOnlyList<ActionInfo> All = new List<ActionInfo>
    {
        new(Login, CategorySessions, "Inicio de sesión (contraseña)"),
        new(LoginFace, CategorySessions, "Inicio de sesión (rostro)"),
        new(LoginQr, CategorySessions, "Inicio de sesión (QR)"),
        new(LoginFailed, CategorySessions, "Inicio de sesión fallido"),
        new(Logout, CategorySessions, "Cierre de sesión"),
        new(SessionRevoked, CategorySessions, "Sesión cerrada por administrador"),
        new(Register, CategorySessions, "Registro de cuenta"),

        new(UserCreated, CategoryUsers, "Usuario creado"),
        new(UserUpdated, CategoryUsers, "Usuario modificado"),
        new(RoleChanged, CategoryUsers, "Cambio de rol"),
        new(UserDisabled, CategoryUsers, "Usuario desactivado"),
        new(UserEnabled, CategoryUsers, "Usuario activado"),
        new(UserDeleted, CategoryUsers, "Usuario eliminado"),
        new(ProfileChangeRequested, CategoryUsers, "Solicitud de corrección creada"),
        new(ProfileChangeApproved, CategoryUsers, "Solicitud de corrección aprobada"),
        new(ProfileChangeRejected, CategoryUsers, "Solicitud de corrección rechazada"),

        new(ProfileUpdated, CategoryProfile, "Perfil actualizado"),
        new(PhotoUpdated, CategoryProfile, "Fotografía actualizada"),
        new(CredentialDesignUpdated, CategoryProfile, "Diseño de credencial guardado"),
        new(CredentialDesignReset, CategoryProfile, "Diseño de credencial restaurado"),
        new(StickerUploaded, CategoryProfile, "Sticker subido"),
        new(StickerDeleted, CategoryProfile, "Sticker eliminado"),

        new(PasswordChanged, CategorySecurity, "Contraseña cambiada"),
        new(PasswordChangeFailed, CategorySecurity, "Cambio de contraseña fallido"),
        new(PasswordResetRequested, CategorySecurity, "Solicitud de restablecimiento"),
        new(PasswordResetCompleted, CategorySecurity, "Contraseña restablecida"),
        new(PasswordResetFailed, CategorySecurity, "Restablecimiento fallido"),
        new(CredentialCreated, CategorySecurity, "Credencial QR emitida"),
        new(CredentialRegenerated, CategorySecurity, "Credencial QR regenerada"),
        new(BiometricRegistered, CategorySecurity, "Rostro registrado"),
        new(BiometricDeleted, CategorySecurity, "Rostro eliminado"),

        new(AnalysisCreated, CategoryAnalysis, "Análisis creado"),
        new(AnalysisDeleted, CategoryAnalysis, "Análisis eliminado"),
        new(ReportSent, CategoryAnalysis, "Reporte enviado"),

        new(DictionaryEntryCreated, CategorySystem, "Entrada de diccionario agregada"),
        new(DictionaryEntryDeleted, CategorySystem, "Entrada de diccionario eliminada")
    };

    public static IReadOnlyList<string> ActionsOf(string category) =>
        All.Where(x => x.Category == category)
            .Select(x => x.Action)
            .ToList();
}
