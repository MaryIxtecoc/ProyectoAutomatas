using AutomatasFinal.Application.Analysis;
using AutomatasFinal.Infrastructure.Analysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Parametrización del sistema: diccionario de entidades del analizador léxico.
/// Exclusivo de ADMIN. (La lectura para el análisis sigue en GET /api/Analysis/dictionary.)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class EntityDictionaryController : ControllerBase
{
    private readonly EntityDictionaryAdminService _service;

    public EntityDictionaryController(EntityDictionaryAdminService service)
    {
        _service = service;
    }

    [HttpGet("options")]
    public IActionResult Options() => Ok(new
    {
        entityTypes = EntityDictionaryAdminService.EntityTypes,
        languages = EntityDictionaryAdminService.Languages
    });

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? entityType,
        [FromQuery] string? language,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (search is { Length: > 80 })
        {
            return BadRequest(new { message = "La búsqueda no puede superar 80 caracteres." });
        }

        return Ok(await _service.ListAsync(entityType, language, search, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EntityDictionaryEntryRequest request)
    {
        try
        {
            var entry = await _service.CreateAsync(request);
            return Ok(new { message = "Entrada agregada al diccionario.", id = entry.Id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        return await _service.DeleteAsync(id)
            ? Ok(new { message = "Entrada eliminada." })
            : NotFound(new { message = "La entrada no existe." });
    }
}
