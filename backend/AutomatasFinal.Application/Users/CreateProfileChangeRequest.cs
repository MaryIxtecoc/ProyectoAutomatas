namespace AutomatasFinal.Application.Users;

/// <summary>
/// Lo único que el usuario puede enviar. user_id, status, reviewed_by y
/// reviewed_at los controla el backend (no existen en este DTO).
/// </summary>
public class CreateProfileChangeRequest
{
    public string? RequestedEmail { get; set; }
    public DateOnly? RequestedBirthDate { get; set; }
    public string Reason { get; set; } = null!;
}

public class ReviewProfileChangeRequest
{
    /// <summary>Comentario opcional del revisor (se guarda en la bitácora).</summary>
    public string? Comment { get; set; }
}
