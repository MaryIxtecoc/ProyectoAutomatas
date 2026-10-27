using AutomatasFinal.Infrastructure.Audit;
using System.Security.Claims;
using AutomatasFinal.Application.Analysis;
using AutomatasFinal.Infrastructure.Analysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalysisController : ControllerBase
{
    private readonly AnalysisService _analysisService;
    private readonly EntityDictionaryService _entityDictionaryService;
    private readonly AuditService _audit;

    public AnalysisController(
        AnalysisService analysisService,
        EntityDictionaryService entityDictionaryService,
        AuditService audit)
    {
        _analysisService = analysisService;
        _entityDictionaryService = entityDictionaryService;
        _audit = audit;
    }

    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> Create(
            IFormFile file,
            [FromForm] string language,
            [FromForm] bool reportRequested = false)
    {
        try
        {
            var userIdClaim = User.FindFirst(
                ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new
                {
                    message = "Token de usuario inválido."
                });
            }

            var request = new CreateAnalysisRequest
            {
                Language = language,
                ReportRequested = reportRequested
            };

            var job = await _analysisService.CreateAsync(
                userId,
                request,
                file);

            await _audit.LogAsync(
                AuditActions.AnalysisCreated,
                true,
                userId,
                details: new { analysisId = job.Id, fileName = job.FileName, language = job.Language.ToString() });

            return CreatedAtAction(
                nameof(GetById),
                new { id = job.Id },
                new
                {
                    id = job.Id,
                    fileName = job.FileName,
                    language = job.Language.ToString(),
                    status = job.Status.ToString(),
                    reportRequested = job.ReportRequested,
                    createdAt = job.CreatedAt
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet("dictionary")]
    public async Task<IActionResult> GetDictionary()
    {
        var entries = await _entityDictionaryService.GetAllAsync();

        return Ok(entries);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var jobs = await _analysisService.GetAllAsync(userId);

        return Ok(jobs.Select(job => new
        {
            id = job.Id,
            fileName = job.FileName,
            language = job.Language.ToString(),
            status = job.Status.ToString(),
            reportRequested = job.ReportRequested,
            errorMessage = job.ErrorMessage,
            uploadedAt = job.UploadedAt,
            processedAt = job.ProcessedAt,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var job = await _analysisService.GetByIdAsync(
            id,
            userId);

        if (job is null)
        {
            return NotFound(new
            {
                message = "Análisis no encontrado."
            });
        }

        return Ok(new
        {
            id = job.Id,
            userId = job.UserId,
            fileName = job.FileName,
            fileStoragePath = job.FileStoragePath,
            language = job.Language.ToString(),
            status = job.Status.ToString(),
            reportRequested = job.ReportRequested,
            reportPdfPath = job.ReportPdfPath,
            errorMessage = job.ErrorMessage,
            uploadedAt = job.UploadedAt,
            processedAt = job.ProcessedAt,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,

            results = job.Results.Select(result => new
            {
                id = result.Id,
                jobId = result.JobId,
                totalWordCount = result.TotalWordCount,
                mostFrequentWords = result.MostFrequentWords,
                leastFrequentWords = result.LeastFrequentWords,
                personalPronouns = result.PersonalPronouns,
                personNames = result.PersonNames,
                rootNouns = result.RootNouns,
                rootVerbs = result.RootVerbs,
                otherClassifications = result.OtherClassifications,
                fullResult = result.FullResult,
                createdAt = result.CreatedAt
            })
        });
    }

    // Devuelve el texto del archivo original para mostrarlo como vista previa
    // en la página de resultados.
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> GetOriginalFile(Guid id)
    {
        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var job = await _analysisService.GetByIdAsync(
            id,
            userId);

        if (job is null)
        {
            return NotFound(new
            {
                message = "Análisis no encontrado."
            });
        }

        if (string.IsNullOrWhiteSpace(job.FileStoragePath))
        {
            return NotFound(new
            {
                message = "El archivo original no está disponible."
            });
        }

        try
        {
            var bytes = await _analysisService.DownloadOriginalFileAsync(job);

            return File(
                bytes,
                "text/plain; charset=utf-8",
                job.FileName);
        }
        catch (Exception)
        {
            return NotFound(new
            {
                message = "El archivo original no está disponible."
            });
        }
    }

    [HttpPost("{id:guid}/process")]
    public async Task<IActionResult> Process(Guid id)
    {
        var userIdClaim = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            await _analysisService.ProcessAsync(
                id,
                userId);

            return Ok(new
            {
                message = "Análisis procesado correctamente.",
                id
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new
            {
                message = ex.Message
            });
        }
    }



    [HttpGet("{id:guid}/report")]
    public async Task<IActionResult> DownloadReport(Guid id)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { message = "Usuario no válido." });

        var job = await _analysisService.GetByIdAsync(id, userId);

        if (job is null)
            return NotFound(new { message = "Análisis no encontrado." });

        // Si el PDF no se generó al procesar (casilla desmarcada o error de
        // fuentes), se genera ahora.
        var reportPath = await _analysisService.EnsureReportAsync(job);

        if (string.IsNullOrWhiteSpace(reportPath))
            return NotFound(new
            {
                message = "Este análisis no tiene un reporte PDF generado."
            });

        var pdfBytes = await _analysisService.DownloadReportAsync(
            reportPath);

        return File(
            pdfBytes,
            "application/pdf",
            $"{Path.GetFileNameWithoutExtension(job.FileName)}-reporte.pdf");
    }

    [HttpPost("{id:guid}/send-report")]
    public async Task<IActionResult> SendReport(
        Guid id,
        [FromBody] SendAnalysisReportRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Usuario no válido." });
        }

        try
        {
            await _analysisService.SendReportAsync(id, userId, request.Channel);

            await _audit.LogAsync(
                AuditActions.ReportSent,
                true,
                userId,
                details: new { analysisId = id, channel = request.Channel.ToUpperInvariant() });

            return Ok(new
            {
                message = $"Reporte enviado por {request.Channel.ToUpperInvariant()}."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

[HttpDelete("{id:guid}")]
public async Task<IActionResult> DeleteAnalysis(Guid id)
{
    var userIdClaim = User.FindFirst(
        System.Security.Claims.ClaimTypes.NameIdentifier
    )?.Value;

    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Unauthorized(new
        {
            message = "Usuario no válido."
        });
    }

    var deleted = await _analysisService.DeleteAsync(
        id,
        userId);

    if (!deleted)
    {
        return NotFound(new
        {
            message = "Análisis no encontrado."
        });
    }

    await _audit.LogAsync(
        AuditActions.AnalysisDeleted,
        true,
        userId,
        details: new { analysisId = id });

    return Ok(new
    {
        message = "Análisis eliminado correctamente."
    });
}

[HttpPost("{id:guid}/retry")]
public async Task<IActionResult> RetryAnalysis(Guid id)
{
    var userIdClaim = User.FindFirst(
        System.Security.Claims.ClaimTypes.NameIdentifier
    )?.Value;

    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Unauthorized(new
        {
            message = "Usuario no válido."
        });
    }

    try
    {
        var retried = await _analysisService.RetryAsync(
            id,
            userId);

        if (!retried)
        {
            return BadRequest(new
            {
                message =
                    "El análisis no puede ser reintentado o ya fue completado."
            });
        }

        return Ok(new
        {
            message = "El análisis está listo para ser procesado nuevamente."
        });
    }
    catch (InvalidOperationException ex)
    {
        return Conflict(new
        {
            message = ex.Message
        });
    }
}

}