using AutomatasFinal.Infrastructure.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutomatasFinal.API.Controllers;

/// <summary>
/// Sirve los stickers que se pueden poner sobre la foto en el registro.
/// No hay tabla en BD para esto: los stickers "oficiales" son simplemente
/// los archivos que se suban a la carpeta stickers/global/ en Supabase
/// Storage. Es público (sin login) porque se necesita durante el registro,
/// antes de que exista una cuenta.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StickersController : ControllerBase
{
    private const string Bucket = "stickers";
    private const string GlobalFolder = "global";

    private readonly SupabaseStorageService _storage;

    public StickersController(SupabaseStorageService storage)
    {
        _storage = storage;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        try
        {
            var files = await _storage.ListAsync(Bucket, GlobalFolder);

            var stickers = files
                .Where(IsImageFile)
                .OrderBy(x => x)
                .Select(fileName => new { fileName })
                .ToList();

            return Ok(stickers);
        }
        catch
        {
            // Si el bucket todavía no existe o está vacío, no rompemos el
            // registro: simplemente no hay stickers para elegir por ahora.
            return Ok(Array.Empty<object>());
        }
    }

    [AllowAnonymous]
    [HttpGet("image/{fileName}")]
    public async Task<IActionResult> GetImage(string fileName)
    {
        if (!IsImageFile(fileName) || fileName.Contains('/') || fileName.Contains(".."))
        {
            return BadRequest();
        }

        try
        {
            var bytes = await _storage.DownloadAsync(Bucket, $"{GlobalFolder}/{fileName}");
            return File(bytes, GetContentType(fileName));
        }
        catch
        {
            return NotFound();
        }
    }

    private static bool IsImageFile(string fileName) =>
        fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
