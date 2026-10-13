using Microsoft.Extensions.Configuration;
using Supabase;

namespace AutomatasFinal.Infrastructure.Storage;

public class SupabaseStorageService
{
    private readonly Supabase.Client _supabase;

    public SupabaseStorageService(IConfiguration configuration)
    {
        var url = configuration["Supabase:Url"];
        var key = configuration["Supabase:SecretKey"];

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "No se encontró Supabase:Url.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "No se encontró Supabase:SecretKey.");
        }

        _supabase = new Supabase.Client(url, key);
    }

    public async Task UploadAsync(
        string bucketName,
        string path,
        Stream fileStream,
        string contentType)
    {
        await using var memoryStream = new MemoryStream();

        await fileStream.CopyToAsync(memoryStream);

        var bytes = memoryStream.ToArray();

        var bucket = _supabase.Storage.From(bucketName);

        await bucket.Upload(
            bytes,
            path,
            new Supabase.Storage.FileOptions
            {
                ContentType = contentType,
                Upsert = true
            });
    }

        public async Task<byte[]> DownloadAsync(
            string bucketName,
            string path)
        {
            var bucket = _supabase.Storage.From(bucketName);

            return await bucket.Download(
                path,
                transformOptions: null,
                onProgress: null,
                cancellationToken: CancellationToken.None);
        }

    public async Task DeleteAsync(
        string bucketName,
        string path)
    {
        var bucket = _supabase.Storage.From(bucketName);

        await bucket.Remove(new List<string>
        {
            path
        });
    }

    /// <summary>
    /// Lista los nombres de archivo dentro de una carpeta de un bucket
    /// (no incluye subcarpetas). Se usa para listar los stickers
    /// disponibles en `stickers/global/` sin necesitar una tabla en BD.
    /// </summary>
    public async Task<List<string>> ListAsync(
        string bucketName,
        string path)
    {
        var bucket = _supabase.Storage.From(bucketName);

        var files = await bucket.List(path);

        return files
            .Where(f => !string.IsNullOrEmpty(f.Name) && f.Id != null)
            .Select(f => f.Name!)
            .ToList();
    }

    /// <summary>
    /// Genera una URL firmada temporal para un archivo de un bucket privado.
    /// Se usa para adjuntar documentos (por ejemplo el PDF de la credencial o de un
    /// reporte) en mensajes de WhatsApp, que requieren una URL pública en vez de bytes.
    /// </summary>
    public async Task<string> CreateSignedUrlAsync(
        string bucketName,
        string path,
        int expiresInSeconds = 3600)
    {
        var bucket = _supabase.Storage.From(bucketName);

        return await bucket.CreateSignedUrl(path, expiresInSeconds);
    }
}