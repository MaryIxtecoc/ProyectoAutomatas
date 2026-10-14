namespace AutomatasFinal.Infrastructure.Notifications;

public interface IWhatsAppSender
{
    /// <param name="toPhone">Número del destinatario. Puede venir con o sin "+"; el sender lo normaliza.</param>
    /// <param name="body">Texto del mensaje.</param>
    /// <param name="mediaUrl">URL pública opcional (por ejemplo un PDF) para adjuntar al mensaje.</param>
    Task SendAsync(
        string toPhone,
        string body,
        string? mediaUrl = null,
        CancellationToken cancellationToken = default);
}
