using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutomatasFinal.Infrastructure.Notifications;

/// <summary>
/// Envía notificaciones (credencial, reset de contraseña) sin bloquear la
/// respuesta HTTP. El registro tiene un límite de 30s y el reset de 30s
/// (ver el documento del proyecto); esperar a que SMTP/Twilio respondan
/// antes de contestar al cliente pone en riesgo ese tiempo, así que el
/// envío se dispara en un scope de DI nuevo e independiente del request.
///
/// Es un fire-and-forget simple (Task.Run + scope propio), no una cola
/// persistente: si el proceso se reinicia justo en ese instante, ese envío
/// puntual se pierde (no la cuenta ni el token, que ya quedaron guardados
/// antes de llamar aquí). Para un entorno con más exigencia de entrega
/// garantizada, lo siguiente sería un IHostedService con una cola
/// (Channel&lt;T&gt;), pero para este proyecto es más complejidad de la
/// que hace falta.
/// </summary>
public class NotificationDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void QueueCredential(Guid userId, byte[] pdfBytes, string pdfBucket, string pdfPath)
    {
        Dispatch(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
            if (user is null) return;

            var notifications = services.GetRequiredService<INotificationService>();
            await notifications.SendCredentialAsync(user, pdfBytes, pdfBucket, pdfPath);
        }, "credencial", userId);
    }

    public void QueuePasswordReset(Guid userId, string rawToken)
    {
        Dispatch(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
            if (user is null) return;

            var notifications = services.GetRequiredService<INotificationService>();
            await notifications.SendPasswordResetAsync(user, rawToken);
        }, "reset de contraseña", userId);
    }

    private void Dispatch(Func<IServiceProvider, Task> work, string label, Guid userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await work(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Falló el envío en segundo plano de {Label} para el usuario {UserId}.",
                    label,
                    userId);
            }
        });
    }
}
