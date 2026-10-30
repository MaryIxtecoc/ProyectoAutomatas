using AutomatasFinal.Infrastructure.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Endpoint SOLO para probar el envío de WhatsApp en desarrollo.
/// En producción responde 404 para que nadie lo use para mandar mensajes.
///
/// Ejemplo (Postman / Swagger):
///   POST /api/WhatsAppTest
///   { "phone": "45113021", "text": "Hola desde Léxica" }
///   { "phone": "45113021", "text": "Aquí va un PDF", "mediaUrl": "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf" }
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WhatsAppTestController : ControllerBase
{
    private readonly IWhatsAppSender _whatsApp;
    private readonly IWebHostEnvironment _env;

    public WhatsAppTestController(IWhatsAppSender whatsApp, IWebHostEnvironment env)
    {
        _whatsApp = whatsApp;
        _env = env;
    }

    public record WhatsAppTestRequest(string Phone, string Text, string? MediaUrl);

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] WhatsAppTestRequest request, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "phone y text son obligatorios." });
        }

        try
        {
            await _whatsApp.SendAsync(request.Phone, request.Text, request.MediaUrl, ct);
            return Ok(new
            {
                message = "Enviado",
                number = EvolutionWhatsAppSender.NormalizePhone(request.Phone)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "No se pudo enviar el WhatsApp.", error = ex.Message });
        }
    }
}
