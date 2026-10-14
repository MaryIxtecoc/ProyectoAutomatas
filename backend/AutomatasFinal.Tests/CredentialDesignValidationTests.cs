using AutomatasFinal.Application.Credentials;
using AutomatasFinal.Infrastructure.Credentials;

namespace AutomatasFinal.Tests;

public class CredentialDesignValidationTests
{
    private static CredentialDesignDto Valid() => new()
    {
        Template = "classic",
        BackgroundColor = "#ffffff",
        AccentColor = "#0a162a",
        TextColor = "#101828",
        PhotoStyle = "circle",
        QrStyle = "classic"
    };

    [Fact]
    public void El_diseno_por_defecto_es_valido()
    {
        CredentialDesignService.Validate(new CredentialDesignDto());
    }

    [Fact]
    public void Normaliza_mayusculas_de_valores_y_colores()
    {
        var result = CredentialDesignService.Validate(Valid());

        Assert.Equal("CLASSIC", result.Template);
        Assert.Equal("CIRCLE", result.PhotoStyle);
        Assert.Equal("#FFFFFF", result.BackgroundColor);
    }

    [Theory]
    [InlineData("Template", "HACK")]
    [InlineData("PhotoStyle", "TRIANGLE")]
    [InlineData("QrStyle", "INVISIBLE")]
    [InlineData("BackgroundColor", "red")]
    [InlineData("BackgroundColor", "#12345")]
    [InlineData("TextColor", "#GGGGGG")]
    public void Rechaza_valores_fuera_de_lo_permitido(string field, string value)
    {
        var dto = Valid();
        typeof(CredentialDesignDto).GetProperty(field)!.SetValue(dto, value);

        Assert.Throws<ArgumentException>(() => CredentialDesignService.Validate(dto));
    }

    [Fact]
    public void Rechaza_texto_sin_contraste_con_el_fondo()
    {
        var dto = Valid();
        dto.TextColor = "#FAFAFA";

        Assert.Throws<ArgumentException>(() => CredentialDesignService.Validate(dto));
    }

    [Fact]
    public void Rechaza_QR_de_color_acento_si_el_acento_es_claro()
    {
        var dto = Valid();
        dto.QrStyle = "ACCENT";
        dto.AccentColor = "#FFE066";

        Assert.Throws<ArgumentException>(() => CredentialDesignService.Validate(dto));
    }

    [Theory]
    [InlineData("../../otro/x.png")]
    [InlineData("carpeta/x.png")]
    [InlineData("x.exe")]
    [InlineData("")]
    public void Rechaza_nombres_de_sticker_peligrosos(string file)
    {
        var dto = Valid();
        dto.Decorations.Add(new CredentialDecorationDto { Source = "USER", File = file, X = 0.5, Y = 0.5, Size = 0.2 });

        Assert.Throws<ArgumentException>(() => CredentialDesignService.Validate(dto));
    }

    [Fact]
    public void Ajusta_posicion_y_tamano_de_stickers_a_los_limites()
    {
        var dto = Valid();
        dto.Decorations.Add(new CredentialDecorationDto { Source = "GLOBAL", File = "estrella.png", X = 5, Y = -2, Size = 3, Rotation = 999 });

        var d = CredentialDesignService.Validate(dto).Decorations.Single();

        Assert.Equal(1, d.X);
        Assert.Equal(0, d.Y);
        Assert.Equal(0.45, d.Size);
        Assert.Equal(180, d.Rotation);
    }

    [Fact]
    public void Rechaza_mas_stickers_que_el_maximo()
    {
        var dto = Valid();
        for (var i = 0; i <= CredentialDesignService.MaxDecorations; i++)
        {
            dto.Decorations.Add(new CredentialDecorationDto { Source = "GLOBAL", File = "a.png", X = 0.5, Y = 0.5, Size = 0.2 });
        }

        Assert.Throws<ArgumentException>(() => CredentialDesignService.Validate(dto));
    }

    [Fact]
    public void Contraste_entre_blanco_y_negro_es_21()
    {
        Assert.Equal(21, CredentialDesignService.ContrastRatio("#000000", "#FFFFFF"), 1);
        Assert.Equal(1, CredentialDesignService.ContrastRatio("#777777", "#777777"), 3);
    }
}
