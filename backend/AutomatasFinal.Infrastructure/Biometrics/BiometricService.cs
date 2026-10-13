using System.Text.Json;
using AutomatasFinal.Application.Biometrics;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Audit;
using AutomatasFinal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AutomatasFinal.Infrastructure.Biometrics;

public class BiometricService
{
    /// <summary>
    /// Umbral de similitud coseno de SFace para aceptar la identidad.
    /// OpenCV publica 0.363 (calibrado en LFW). En nuestras pruebas, personas
    /// distintas dieron como máximo 0.33, así que se deja margen por encima.
    /// Se puede ajustar con Biometrics:MatchThreshold en la configuración.
    /// </summary>
    public const double FaceMatchThreshold = 0.42;

    private readonly AppDbContext _db;
    private readonly FaceRecognitionService _faceRecognitionService;
    private readonly double _matchThreshold;
    private readonly AuditService _audit;

    public BiometricService(
        AppDbContext db,
        FaceRecognitionService faceRecognitionService,
        IConfiguration configuration,
        AuditService audit)
    {
        _audit = audit;
        _db = db;
        _faceRecognitionService = faceRecognitionService;
        _matchThreshold = configuration.GetValue(
            "Biometrics:MatchThreshold",
            FaceMatchThreshold);
    }

    public async Task SaveFaceImageAsync(
        Guid userId,
        byte[] imageBytes)
    {
        var result =
            _faceRecognitionService.AnalyzeFace(imageBytes);

        ValidateFaceResult(result);

        var descriptor =
            JsonSerializer.Serialize(result.Descriptor);

        var biometric = await _db.UserBiometrics
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (biometric is null)
        {
            biometric = new UserBiometric
            {
                UserId = userId,
                FaceDescriptor = descriptor,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.UserBiometrics.Add(biometric);
        }
        else
        {
            biometric.FaceDescriptor = descriptor;
            biometric.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        // Nunca el descriptor: solo que se registró y con qué confianza se detectó.
        await _audit.LogAsync(
            AuditActions.BiometricRegistered,
            true,
            userId,
            details: new { detectionConfidence = Math.Round(result.Confidence, 3) });
    }

    public async Task<FaceVerificationResult> VerifyFaceAsync(
        Guid userId,
        byte[] imageBytes)
    {
        var biometric = await _db.UserBiometrics
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (biometric is null)
        {
            throw new InvalidOperationException(
                "El usuario no tiene un rostro registrado.");
        }

        var result =
            _faceRecognitionService.AnalyzeFace(imageBytes);

        ValidateFaceResult(result);

        var storedDescriptor =
            JsonSerializer.Deserialize<float[]>(
                biometric.FaceDescriptor);

        if (!FaceRecognitionService.IsValidDescriptor(storedDescriptor))
        {
            throw new InvalidOperationException(
                "El descriptor facial almacenado no es válido.");
        }

        var similarity =
            _faceRecognitionService.Compare(
                storedDescriptor!,
                result.Descriptor);

        return new FaceVerificationResult
        {
            Verified = similarity >= _matchThreshold,
            Similarity = similarity,
            Threshold = _matchThreshold
        };
    }

    public async Task<object?> GetMyBiometricAsync(
        Guid userId)
    {
        var biometric = await _db.UserBiometrics
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (biometric is null)
        {
            return null;
        }

        return new
        {
            registered = true,
            biometric.CreatedAt,
            biometric.UpdatedAt
        };
    }

    public async Task<bool> DeleteFaceDescriptorAsync(
        Guid userId)
    {
        var biometric = await _db.UserBiometrics
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (biometric is null)
        {
            return false;
        }

        _db.UserBiometrics.Remove(biometric);

        await _db.SaveChangesAsync();

        await _audit.LogAsync(
            AuditActions.BiometricDeleted,
            true,
            userId);

        return true;
    }

    private static void ValidateFaceResult(
        FaceRecognitionResult result)
    {
        if (!result.FaceDetected)
        {
            throw new ArgumentException(
                "No se detectó ningún rostro.");
        }

        if (result.MultipleFaces ||
            result.FaceCount != 1)
        {
            throw new ArgumentException(
                "La imagen debe contener exactamente un rostro.");
        }

        if (result.FaceTooSmall)
        {
            throw new ArgumentException(
                "El rostro es demasiado pequeño. Acércate a la cámara.");
        }

        if (!FaceRecognitionService.IsValidDescriptor(result.Descriptor))
        {
            throw new ArgumentException(
                "No fue posible generar el descriptor facial.");
        }
    }
}

public class FaceVerificationResult
{
    public bool Verified { get; set; }

    public double Similarity { get; set; }

    public double Threshold { get; set; }
}