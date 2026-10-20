using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            message = "AutomatasFinal API funcionando correctamente",
            timestamp = DateTime.UtcNow
        });
    }
}