using System.Text;
using System.Text.Json;
using AutomatasFinal.Application.Analysis;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Data;
using AutomatasFinal.Infrastructure.Notifications;
using AutomatasFinal.Infrastructure.Reports;
using AutomatasFinal.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AutomatasFinal.Infrastructure.Analysis;

public class AnalysisService
{
    private readonly AppDbContext _db;
    private readonly PdfReportService _pdfReportService;
    private readonly SupabaseStorageService _storage;
    private readonly EntityDictionaryService _entityDictionaryService;
    private readonly INotificationService _notificationService;
    private readonly NlpServiceClient _nlpClient;

    public AnalysisService(
        AppDbContext db,
        PdfReportService pdfReportService,
        SupabaseStorageService storage,
        EntityDictionaryService entityDictionaryService,
        INotificationService notificationService,
        NlpServiceClient nlpClient)
    {
        _db = db;
        _pdfReportService = pdfReportService;
        _storage = storage;
        _entityDictionaryService = entityDictionaryService;
        _notificationService = notificationService;
        _nlpClient = nlpClient;
    }

    public async Task<AnalysisJob?> GetByIdAsync(
        Guid jobId,
        Guid userId)
    {
        return await _db.AnalysisJobs
            .AsNoTracking()
            .Include(x => x.Results)
            .FirstOrDefaultAsync(x =>
                x.Id == jobId &&
                x.UserId == userId);
    }

    public async Task<AnalysisJob> CreateAsync(
        Guid userId,
        CreateAnalysisRequest request,
        IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException(
                "Debe proporcionar un archivo.");
        }

        if (!Enum.TryParse<AnalysisLanguage>(
                request.Language.Trim(),
                true,
                out var language))
        {
            throw new ArgumentException(
                "Idioma inválido. Valores permitidos: ES, EN, RU, ZH, AR.");
        }

        var extension = Path.GetExtension(file.FileName);

        if (!string.Equals(
                extension,
                ".txt",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Solo se permiten archivos .txt.");
        }

        const long maxFileSize = 5 * 1024 * 1024;

        if (file.Length > maxFileSize)
        {
            throw new ArgumentException(
                "El archivo no puede superar los 5 MB.");
        }

        var userExists = await _db.Users
            .AnyAsync(x =>
                x.Id == userId &&
                x.IsActive &&
                x.DeletedAt == null);

        if (!userExists)
        {
            throw new InvalidOperationException(
                "El usuario no existe o está inactivo.");
        }

        var jobId = Guid.NewGuid();

        var storedFileName = $"{jobId}.txt";

        var storagePath =
            $"{userId}/{storedFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.UploadAsync(
                "analysis-files",
                storagePath,
                stream,
                "text/plain");
        }

        var now = DateTime.UtcNow;

        var job = new AnalysisJob
        {
            Id = jobId,
            UserId = userId,
            FileName = Path.GetFileName(file.FileName),
            FileStoragePath = storagePath,
            Language = language,
            Status = AnalysisStatus.PENDING,
            ReportRequested = request.ReportRequested,
            UploadedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AnalysisJobs.Add(job);

        await _db.SaveChangesAsync();

        return job;
    }

    public async Task ProcessAsync(
        Guid jobId,
        Guid userId)
    {
        var job = await _db.AnalysisJobs
            .FirstOrDefaultAsync(x =>
                x.Id == jobId &&
                x.UserId == userId);

        if (job is null)
        {
            throw new InvalidOperationException(
                "Análisis no encontrado.");
        }

        // Si ya se completó, no se vuelve a procesar
        // (evita resultados duplicados).
        if (job.Status == AnalysisStatus.COMPLETED)
        {
            return;
        }

        // Si está procesándose hace menos de 5 minutos, se rechaza.
        // Si lleva más, se considera trabado y se permite reintentar.
        if (job.Status == AnalysisStatus.PROCESSING)
        {
            var elapsed = DateTime.UtcNow - job.UpdatedAt;

            if (elapsed < TimeSpan.FromMinutes(5))
            {
                throw new InvalidOperationException(
                    "El análisis ya está siendo procesado.");
            }
        }

        try
        {
            job.Status = AnalysisStatus.PROCESSING;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Limpiar resultados de intentos anteriores para
            // que cada reintento empiece limpio.
            var oldResults = await _db.AnalysisResults
                .Where(x => x.JobId == job.Id)
                .ToListAsync();

            if (oldResults.Count > 0)
            {
                _db.AnalysisResults.RemoveRange(oldResults);
            }

            var fileBytes = await _storage.DownloadAsync(
                "analysis-files",
                job.FileStoragePath);

            var text = Encoding.UTF8.GetString(fileBytes);

            // Cargar el diccionario de entidades desde Supabase/PostgreSQL.
            var dictionary = await _entityDictionaryService.GetAllAsync(
                job.Language.ToString());

            var analyzer = new TextAnalyzer();

            var analysis = analyzer.Analyze(
                text,
                job.Language,
                dictionary);

            // Análisis lingüístico con spaCy/Stanza (servicio nlp-service).
            // Si el servicio no está disponible, se conserva el resultado del
            // analizador propio: el análisis nunca se queda sin resultado.
            var nlpResult = await _nlpClient.AnalyzeAsync(
                text,
                job.Language);

            if (nlpResult is not null)
            {
                analysis = NlpResultMerger.Merge(
                    analysis,
                    nlpResult,
                    job.Language);
            }
            else
            {
                NlpResultMerger.MarkManual(analysis);
            }

            var result = new AnalysisResult
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                TotalWordCount = analysis.TotalWordCount,

                MostFrequentWords =
                    JsonSerializer.Serialize(
                        analysis.MostFrequentWords),

                LeastFrequentWords =
                    JsonSerializer.Serialize(
                        analysis.LeastFrequentWords),

                PersonalPronouns =
                    JsonSerializer.Serialize(
                        analysis.PersonalPronouns),

                PersonNames =
                    JsonSerializer.Serialize(
                        analysis.PersonNames),

                RootNouns =
                    JsonSerializer.Serialize(
                        analysis.RootNouns),

                RootVerbs =
                    JsonSerializer.Serialize(
                        analysis.RootVerbs),

                OtherClassifications =
                    JsonSerializer.Serialize(
                        analysis.OtherClassifications),

                FullResult =
                    JsonSerializer.Serialize(analysis),

                CreatedAt = DateTime.UtcNow
            };

            _db.AnalysisResults.Add(result);

            job.Status = AnalysisStatus.COMPLETED;
            job.ProcessedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            if (job.ReportRequested)
            {
                // Si el PDF falla (por ejemplo, una fuente sin caracteres
                // chinos o árabes), el análisis NO se marca como fallido: los
                // resultados ya están guardados y el PDF se puede volver a
                // generar con "Descargar PDF" o "Enviar por correo".
                try
                {
                    var reportPath =
                        await _pdfReportService.GenerateAsync(
                            job,
                            result);

                    job.ReportPdfPath = reportPath;
                    job.UpdatedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                }
                catch (Exception reportEx)
                {
                    Console.Error.WriteLine(
                        $"[AnalysisService] No se pudo generar el PDF del análisis {job.Id}: {reportEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            job.Status = AnalysisStatus.FAILED;
            job.ErrorMessage = ex.Message;
            job.ProcessedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            throw;
        }
    }


    public async Task<List<AnalysisJob>> GetAllAsync(
        Guid userId)
    {
        return await _db.AnalysisJobs
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Devuelve la ruta del PDF del análisis; si todavía no existe y el
    /// análisis está completo, lo genera en ese momento.
    /// </summary>
    public async Task<string?> EnsureReportAsync(AnalysisJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.ReportPdfPath))
            return job.ReportPdfPath;

        if (job.Status != AnalysisStatus.COMPLETED)
            return null;

        var result = await _db.AnalysisResults
            .FirstOrDefaultAsync(x => x.JobId == job.Id);

        if (result is null)
            return null;

        job.ReportPdfPath = await _pdfReportService.GenerateAsync(job, result);
        job.UpdatedAt = DateTime.UtcNow;

        _db.AnalysisJobs.Update(job);
        await _db.SaveChangesAsync();

        return job.ReportPdfPath;
    }

    public async Task<byte[]> DownloadReportAsync(string reportPath)
    {
        return await _storage.DownloadAsync(
            "analysis-reports",
            reportPath);
    }

    /// <summary>
    /// Envía el reporte de análisis ya generado por correo o WhatsApp, respetando
    /// el/los canal(es) que el usuario eligió en su registro.
    /// </summary>
    public async Task SendReportAsync(Guid jobId, Guid userId, string channel)
    {
        if (!Enum.TryParse<NotificationMethod>(channel?.Trim(), true, out var requestedChannel) ||
            requestedChannel == NotificationMethod.BOTH)
        {
            throw new ArgumentException(
                "Canal inválido. Usa EMAIL o WHATSAPP.");
        }

        var job = await _db.AnalysisJobs
            .FirstOrDefaultAsync(x =>
                x.Id == jobId &&
                x.UserId == userId);

        if (job is null)
        {
            throw new InvalidOperationException(
                "Análisis no encontrado.");
        }

        // Si al crear el análisis no se pidió el PDF, se genera ahora: al
        // terminar el análisis el sitio pregunta si se quiere recibir.
        if (string.IsNullOrWhiteSpace(job.ReportPdfPath))
        {
            var result = job.Status == AnalysisStatus.COMPLETED
                ? await _db.AnalysisResults.FirstOrDefaultAsync(x => x.JobId == job.Id)
                : null;

            if (result is null)
            {
                throw new InvalidOperationException(
                    "El análisis todavía no tiene resultados para generar el reporte.");
            }

            job.ReportPdfPath = await _pdfReportService.GenerateAsync(job, result);
            job.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new InvalidOperationException(
                "Usuario no encontrado.");
        }

        // Solo se permite el canal (o canales) que el usuario eligió al registrarse.
        var allowed =
            user.NotificationMethod == NotificationMethod.BOTH ||
            user.NotificationMethod == requestedChannel;

        if (!allowed)
        {
            throw new ArgumentException(
                $"Tu método de notificación registrado es {user.NotificationMethod}; " +
                $"no es posible enviarte el reporte por {requestedChannel}.");
        }

        var pdfBytes = await _storage.DownloadAsync(
            "analysis-reports",
            job.ReportPdfPath);

        var fileName = $"{Path.GetFileNameWithoutExtension(job.FileName)}-reporte.pdf";

        await _notificationService.SendAnalysisReportAsync(
            user,
            pdfBytes,
            "analysis-reports",
            job.ReportPdfPath,
            fileName,
            requestedChannel);
    }

    public async Task<bool> DeleteAsync(
        Guid jobId,
        Guid userId)
    {
        var job = await _db.AnalysisJobs
            .FirstOrDefaultAsync(x =>
                x.Id == jobId &&
                x.UserId == userId);

        if (job is null)
        {
            return false;
        }

        // Eliminar archivo TXT de Supabase Storage
        if (!string.IsNullOrWhiteSpace(job.FileStoragePath))
        {
            try
            {
                await _storage.DeleteAsync(
                    "analysis-files",
                    job.FileStoragePath);
            }
            catch
            {
                // Si el archivo ya no existe en Storage,
                // no impedimos eliminar el registro.
            }
        }

        // Eliminar PDF de Supabase Storage
        if (!string.IsNullOrWhiteSpace(job.ReportPdfPath))
        {
            try
            {
                await _storage.DeleteAsync(
                    "analysis-reports",
                    job.ReportPdfPath);
            }
            catch
            {
                // Si el PDF ya no existe, continuamos.
            }
        }

        // Eliminar resultado asociado
        var results = await _db.AnalysisResults
            .Where(x => x.JobId == jobId)
            .ToListAsync();

        if (results.Count > 0)
        {
            _db.AnalysisResults.RemoveRange(results);
        }

        // Finalmente eliminar el análisis
        _db.AnalysisJobs.Remove(job);

        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RetryAsync(Guid jobId, Guid userId)
    {
        var job = await _db.AnalysisJobs
            .FirstOrDefaultAsync(x =>
                x.Id == jobId &&
                x.UserId == userId);

        if (job is null)
            return false;

        if (job.Status == AnalysisStatus.COMPLETED)
            return false;

        if (job.Status == AnalysisStatus.PROCESSING)
        {
            var elapsed = DateTime.UtcNow - job.UpdatedAt;

            if (elapsed < TimeSpan.FromMinutes(5))
            {
                throw new InvalidOperationException(
                    "El análisis todavía está siendo procesado.");
            }
        }

        job.Status = AnalysisStatus.PENDING;
        job.ErrorMessage = null;
        job.ProcessedAt = null;
        job.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return true;
    }
}