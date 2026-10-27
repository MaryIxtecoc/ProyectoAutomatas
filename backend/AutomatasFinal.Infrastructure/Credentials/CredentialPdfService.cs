using AutomatasFinal.Application.Credentials;
using AutomatasFinal.Domain.Entities;
using OpenCvSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutomatasFinal.Infrastructure.Credentials;

/// <summary>
/// Dibuja la credencial (una página, 8.5 x 5.5 cm). La identidad (nickname,
/// email, rol, foto, QR, fecha) llega de la BD; el diseño solo aporta colores,
/// plantilla, estilo de foto y stickers, ya validados por CredentialDesignService.
/// </summary>
public class CredentialPdfService
{
    private const float CardWidthCm = 8.5f;
    private const float CardHeightCm = 5.5f;
    private const float PointsPerCm = 28.3464567f;

    private const string FooterText =
        "Esta credencial es personal e intransferible. " +
        "AutomatasFinal - Proyecto Final de Lenguajes Formales 2026.";

    public static float CardWidth => CardWidthCm * PointsPerCm;
    public static float CardHeight => CardHeightCm * PointsPerCm;

    private static readonly byte[]? UmgLogo = LoadUmgLogo();

    private static byte[]? LoadUmgLogo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "umg-logo.png");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static (float X, float Y, float Size) LogoPlacement(string template, float cardWidth, float cardHeight) =>
        template switch
        {
            "LATERAL" => (cardWidth - 24 - 8, 6, 24),
            "MINIMAL" => (cardWidth - 20 - 10, cardHeight - 20 - 4, 20),
            _ => (cardWidth - 26 - 10, 4, 26)
        };

    public byte[] Generate(
        User user,
        string roleName,
        byte[] photoBytes,
        byte[] qrPngBytes,
        DateTime issuedAtUtc,
        CredentialDesignDto design,
        IReadOnlyList<(CredentialDecorationDto Decoration, byte[] Bytes)> stickers,
        string qrCaption = "Escanéame para\niniciar sesión")
    {
        var cardWidth = CardWidth;
        var cardHeight = CardHeight;

        var styledPhoto = StylePhoto(photoBytes, design.PhotoStyle);

        var card = new CardData(
            user.Nickname,
            user.Email,
            roleName,
            issuedAtUtc.ToString("dd/MM/yyyy"),
            styledPhoto,
            design.PhotoStyle == "SQUARE",
            qrPngBytes,
            qrCaption,
            design.BackgroundColor,
            design.AccentColor,
            design.TextColor,
            OnColor(design.AccentColor));

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(new PageSize(cardWidth, cardHeight));
                page.Margin(0);

                page.Content()
                    .Width(cardWidth)
                    .Height(cardHeight)
                    .Layers(layers =>
                    {
                        layers.PrimaryLayer().Element(c =>
                        {
                            switch (design.Template)
                            {
                                case "LATERAL":
                                    ComposeLateral(c, card);
                                    break;
                                case "MINIMAL":
                                    ComposeMinimal(c, card);
                                    break;
                                default:
                                    ComposeClassic(c, card);
                                    break;
                            }
                        });

                        if (UmgLogo is not null)
                        {
                            var (logoX, logoY, logoSize) = LogoPlacement(design.Template, cardWidth, cardHeight);

                            layers.Layer()
                                .OffsetX(logoX)
                                .OffsetY(logoY)
                                .Width(logoSize)
                                .Height(logoSize)
                                .Image(UmgLogo)
                                .FitArea();
                        }

                        foreach (var (decoration, bytes) in stickers)
                        {
                            var side = (float)(decoration.Size * cardWidth);
                            var centerX = (float)(decoration.X * cardWidth);
                            var centerY = (float)(decoration.Y * cardHeight);

                            // Se traslada al centro, se rota y se vuelve medio lado:
                            // la rotación queda alrededor del centro del sticker.
                            layers.Layer()
                                .TranslateX(centerX)
                                .TranslateY(centerY)
                                .Rotate((float)decoration.Rotation)
                                .TranslateX(-side / 2)
                                .TranslateY(-side / 2)
                                .Width(side)
                                .Height(side)
                                .Image(bytes)
                                .FitArea();
                        }
                    });
            });
        });

        using var stream = new MemoryStream();

        document.GeneratePdf(stream);

        return stream.ToArray();
    }

    private record CardData(
        string Nickname,
        string Email,
        string RoleName,
        string IssuedAt,
        byte[] Photo,
        bool PhotoBorder,
        byte[] Qr,
        string QrCaption,
        string Background,
        string Accent,
        string Text,
        string OnAccent);

    // Diseño original: banda superior de acento + cuerpo + pie.
    private static void ComposeClassic(IContainer container, CardData d)
    {
        container.Background(d.Background).Column(column =>
        {
            column.Item()
                .Height(34)
                .Background(d.Accent)
                .PaddingHorizontal(12)
                .PaddingVertical(2)
                .Column(header =>
                {
                    header.Item().Text("LÉXICA").FontSize(11).Bold().FontColor(d.OnAccent);
                    header.Item().PaddingTop(1).Text("Analiza. Descubre. Comprende.").FontSize(4).FontColor(d.OnAccent);
                    header.Item().PaddingTop(2).Text("CREDENCIAL DE INSCRIPCIÓN").FontSize(5.5f).Bold().FontColor(d.OnAccent);
                });

            column.Item()
                .Height(111)
                .PaddingHorizontal(10)
                .PaddingTop(5)
                .Row(row =>
                {
                    Photo(row.ConstantItem(68).Height(68), d);

                    row.RelativeItem()
                        .PaddingLeft(8)
                        .PaddingRight(4)
                        .AlignMiddle()
                        .Element(c => Info(c, d, 9));

                    row.ConstantItem(58).AlignMiddle().Element(c => Qr(c, d, 58));
                });

            column.Item().Height(10).PaddingHorizontal(10).AlignMiddle().Element(c => Footer(c, d));
        });
    }

    // Banda vertical de acento a la izquierda con la foto.
    private static void ComposeLateral(IContainer container, CardData d)
    {
        container.Background(d.Background).Row(row =>
        {
            row.ConstantItem(82)
                .Background(d.Accent)
                .PaddingVertical(8)
                .PaddingHorizontal(7)
                .Column(band =>
                {
                    band.Item().AlignCenter().Text("LÉXICA").FontSize(10).Bold().FontColor(d.OnAccent);
                    band.Item().AlignCenter().Text("Analiza. Descubre. Comprende.").FontSize(3.4f).FontColor(d.OnAccent);
                    band.Item().PaddingTop(8).AlignCenter().Width(64).Height(64).Element(c => Photo(c, d));
                    band.Item().PaddingTop(6).AlignCenter().Text("CREDENCIAL").FontSize(5).Bold().FontColor(d.OnAccent);
                });

            row.RelativeItem()
                .PaddingHorizontal(9)
                .PaddingTop(10)
                .Column(body =>
                {
                    body.Item().Text("CREDENCIAL DE INSCRIPCIÓN").FontSize(5.5f).Bold().FontColor(d.Text);

                    body.Item().Height(108).Row(inner =>
                    {
                        inner.RelativeItem().PaddingRight(4).AlignMiddle().Element(c => Info(c, d, 9));
                        inner.ConstantItem(54).AlignMiddle().Element(c => Qr(c, d, 54));
                    });

                    body.Item().Element(c => Footer(c, d));
                });
        });
    }

    // Fondo liso, encabezado en texto y una línea fina de acento.
    private static void ComposeMinimal(IContainer container, CardData d)
    {
        container.Background(d.Background).PaddingHorizontal(12).PaddingTop(9).Column(column =>
        {
            column.Item().Row(top =>
            {
                top.RelativeItem().Text("LÉXICA").FontSize(11).Bold().FontColor(d.Text);
                top.AutoItem().AlignBottom().Text("CREDENCIAL DE INSCRIPCIÓN").FontSize(5).Bold().FontColor(d.Text);
            });

            column.Item().PaddingTop(3).Height(2).Background(d.Accent);

            column.Item()
                .Height(106)
                .PaddingTop(6)
                .Row(row =>
                {
                    Photo(row.ConstantItem(66).Height(66), d);

                    row.RelativeItem()
                        .PaddingLeft(8)
                        .PaddingRight(4)
                        .AlignMiddle()
                        .Element(c => Info(c, d, 9));

                    row.ConstantItem(56).AlignMiddle().Element(c => Qr(c, d, 56));
                });

            column.Item().Element(c => Footer(c, d));
        });
    }

    private static void Photo(IContainer container, CardData d)
    {
        var box = d.PhotoBorder
            ? container.Border(1.2f).BorderColor(d.Accent)
            : container;

        box.Image(d.Photo).FitArea();
    }

    private static void Info(IContainer container, CardData d, float nameSize)
    {
        container.Column(info =>
        {
            info.Spacing(2);

            info.Item().Text(d.Nickname).FontSize(nameSize).Bold().FontColor(d.Text).ClampLines(1, "…");
            info.Item().Text(d.Email).FontSize(4.8f).FontColor(d.Text).ClampLines(1, "…");

            info.Item().PaddingTop(2).Text(text =>
            {
                text.Span("Rol: ").Bold().FontSize(5.2f).FontColor(d.Text);
                text.Span(d.RoleName).FontSize(5.2f).FontColor(d.Text);
            });

            info.Item().Text(text =>
            {
                text.Span("Emitida: ").Bold().FontSize(5.2f).FontColor(d.Text);
                text.Span(d.IssuedAt).FontSize(5.2f).FontColor(d.Text);
            });
        });
    }

    private static void Qr(IContainer container, CardData d, float size)
    {
        container.Column(qr =>
        {
            // Fondo blanco propio: el QR se lee bien sobre cualquier color de credencial.
            qr.Item().Width(size).Height(size).Background(Colors.White).Image(d.Qr).FitArea();
            qr.Item().PaddingTop(1).AlignCenter().Text(d.QrCaption).FontSize(3.5f).FontColor(d.Text);
        });
    }

    private static void Footer(IContainer container, CardData d)
    {
        container.Text(FooterText).FontSize(3.2f).FontColor(d.Text).ClampLines(1, "…");
    }

    /// <summary>Texto legible sobre el color de acento: blanco u oscuro según su luminancia.</summary>
    private static string OnColor(string accent) =>
        CredentialDesignService.ContrastRatio(accent, "#FFFFFF") >= 3 ? "#FFFFFF" : "#101828";

    /// <summary>
    /// Recorta la foto al centro en cuadrado y, según el estilo, aplica
    /// máscara redondeada o circular (PNG con transparencia). Se hace con
    /// OpenCvSharp (ya es dependencia del proyecto).
    /// </summary>
    private static byte[] StylePhoto(byte[] photoBytes, string photoStyle)
    {
        using var source = Cv2.ImDecode(photoBytes, ImreadModes.Color);

        if (source.Empty())
        {
            return photoBytes;
        }

        var side = Math.Min(source.Width, source.Height);
        var rect = new Rect((source.Width - side) / 2, (source.Height - side) / 2, side, side);

        using var square = new Mat(source, rect);
        using var resized = new Mat();
        Cv2.Resize(square, resized, new OpenCvSharp.Size(400, 400), 0, 0, InterpolationFlags.Area);

        if (photoStyle == "SQUARE")
        {
            Cv2.ImEncode(".png", resized, out var plain);
            return plain;
        }

        using var bgra = new Mat();
        Cv2.CvtColor(resized, bgra, ColorConversionCodes.BGR2BGRA);

        using var mask = new Mat(400, 400, MatType.CV_8UC1, Scalar.All(0));

        if (photoStyle == "CIRCLE")
        {
            Cv2.Circle(mask, new Point(200, 200), 199, Scalar.All(255), -1, LineTypes.AntiAlias);
        }
        else
        {
            const int radius = 72;
            Cv2.Rectangle(mask, new Rect(radius, 0, 400 - 2 * radius, 400), Scalar.All(255), -1);
            Cv2.Rectangle(mask, new Rect(0, radius, 400, 400 - 2 * radius), Scalar.All(255), -1);
            foreach (var center in new[] { new Point(radius, radius), new Point(399 - radius, radius), new Point(radius, 399 - radius), new Point(399 - radius, 399 - radius) })
            {
                Cv2.Circle(mask, center, radius, Scalar.All(255), -1, LineTypes.AntiAlias);
            }
        }

        var channels = Cv2.Split(bgra);
        try
        {
            mask.CopyTo(channels[3]);
            Cv2.Merge(channels, bgra);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }

        Cv2.ImEncode(".png", bgra, out var styled);
        return styled;
    }
}
