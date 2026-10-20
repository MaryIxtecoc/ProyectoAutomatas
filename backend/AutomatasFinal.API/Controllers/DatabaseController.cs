using AutomatasFinal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabaseController : ControllerBase
{
    private readonly AppDbContext _db;

    public DatabaseController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync();

            if (!canConnect)
            {
                return StatusCode(503, new
                {
                    status = "error",
                    message = "PostgreSQL rechazó la conexión."
                });
            }

            return Ok(new
            {
                status = "ok",
                message = "Conexión con PostgreSQL establecida correctamente."
            });
        }
        catch (Exception ex)
        {
            // El detalle (host, usuario, etc.) solo va a la consola del servidor.
            Console.WriteLine(ex);

            return StatusCode(503, new
            {
                status = "error",
                message = "No fue posible conectar con la base de datos."
            });
        }
    }
}