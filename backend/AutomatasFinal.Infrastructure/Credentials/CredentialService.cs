using System.Security.Cryptography;
using System.Text;
using AutomatasFinal.Application.Credentials;
using AutomatasFinal.Domain.Entities;
using AutomatasFinal.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace AutomatasFinal.Infrastructure.Credentials;

public class CredentialService
{
    private const string CredentialsBucket = "credentials";

    private readonly SupabaseStorageService _storage;
    private readonly CredentialPdfService _pdfService;
    private readonly IConfiguration _configuration;
    private readonly CredentialDesignService _designService;

    public CredentialService(
        SupabaseStorageService storage,
        CredentialPdfService pdfService,
        IConfiguration configuration,
        CredentialDesignService designService)
    {
        _storage = storage;
        _pdfService = pdfService;
        _configuration = configuration;
        _designService = designService;
    }

    /// <summary>
    /// Genera el QR y el PDF de la credencial y sube el PDF a Storage.
    /// Devuelve la entidad <see cref="UserCredential"/> ya lista para agregarse al contexto
    /// (no la guarda: se guarda junto con el resto del registro en una sola transacción)
    /// junto con los bytes del PDF, para poder enviarlos por correo/WhatsApp.
    /// </summary>
    public async Task<(UserCredential Credential, byte[] PdfBytes)> IssueAsync(
        User user,
        string roleName,
        byte[] modifiedPhotoBytes)
    {
        // El QR lleva un secreto aleatorio de 256 bits (base64url). En BD
        // solo se guarda su hash (ver HashQrSecret): quien lea la tabla no
        // puede iniciar sesión con lo que encuentre ahí. El secreto en claro
        // existe únicamente dentro del QR/PDF.
        var qrSecret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        // Lo que se dibuja en el QR sí es una URL completa, para que se
        // pueda abrir directamente con la cámara del teléfono (útil en la
        // demo: cuando el frontend corre detrás de ngrok en vez de
        // localhost, solo hay que cambiar App:FrontendBaseUrl en la
        // configuración -- ese es el "espacio para ngrok" -- y las
        // credenciales que se emitan después ya apuntan ahí).
        var frontendBaseUrl = (_configuration["App:FrontendBaseUrl"] ?? "http://localhost:3000")
            .TrimEnd('/');

        var qrTargetUrl = $"{frontendBaseUrl}/auth/qr?token={qrSecret}";

        // Diseño guardado por el usuario (solo apariencia).
        var design = await _designService.GetForUserAsync(user.Id);
        var stickers = await _designService.LoadDecorationImagesAsync(user.Id, design);

        var qrPngBytes = GenerateQrPng(qrTargetUrl, QrDarkColor(design));

        var credentialId = Guid.NewGuid();
        var issuedAt = DateTime.UtcNow;

        var pdfBytes = _pdfService.Generate(
            user,
            roleName,
            modifiedPhotoBytes,
            qrPngBytes,
            issuedAt,
            design,
            stickers);

        // Un archivo por credencial: regenerar nunca sobrescribe el PDF de la
        // credencial que sigue activa mientras la nueva no quede guardada en BD.
        var pdfPath = $"{user.Id}/{credentialId}.pdf";

        await using (var pdfStream = new MemoryStream(pdfBytes))
        {
            await _storage.UploadAsync(CredentialsBucket, pdfPath, pdfStream, "application/pdf");
        }

        var credential = new UserCredential
        {
            Id = credentialId,
            UserId = user.Id,
            QrCodeValue = HashQrSecret(qrSecret),
            PdfPath = pdfPath,
            IsActive = true,
            IssuedAt = issuedAt
        };

        return (credential, pdfBytes);
    }

    public static string BucketName => CredentialsBucket;

    /// <summary>
    /// PDF de vista previa con un diseño aún no guardado (ya validado). Usa los
    /// datos reales del usuario pero un QR de muestra sin valor: no crea
    /// credencial, no guarda nada y no sirve para iniciar sesión.
    /// </summary>
    public async Task<byte[]> RenderPreviewAsync(
        User user,
        string roleName,
        byte[] modifiedPhotoBytes,
        CredentialDesignDto design)
    {
        var stickers = await _designService.LoadDecorationImagesAsync(user.Id, design);

        var sampleQr = GenerateQrPng("VISTA PREVIA - SIN VALIDEZ", QrDarkColor(design));

        return _pdfService.Generate(
            user,
            roleName,
            modifiedPhotoBytes,
            sampleQr,
            DateTime.UtcNow,
            design,
            stickers,
            "VISTA PREVIA\nsin validez");
    }

    private static byte[] QrDarkColor(CredentialDesignDto design)
    {
        if (design.QrStyle != "ACCENT")
        {
            return new byte[] { 0, 0, 0, 255 };
        }

        var hex = design.AccentColor;
        return new byte[]
        {
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16),
            255
        };
    }

    private const string QrHashPrefix = "sha256:";

    /// <summary>
    /// Valor que se guarda en user_credentials.qr_code_value: "sha256:" + hex
    /// del SHA-256 del secreto. Con 256 bits aleatorios no hace falta sal ni
    /// un hash lento: no hay diccionario posible. Debe coincidir con la
    /// función app.find_user_for_qr_login de database/database.sql.
    /// </summary>
    public static string HashQrSecret(string qrSecret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(qrSecret));
        return QrHashPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] GenerateQrPng(string value, byte[] darkColorRgba)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        // Módulos oscuros (negro o acento validado) sobre blanco, con zona de silencio.
        return pngQrCode.GetGraphic(20, darkColorRgba, new byte[] { 255, 255, 255, 255 }, true);
    }
}
