using AutomatasFinal.Application.Security;

namespace AutomatasFinal.Tests;

public class FieldLimitsTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("carmen")]
    [InlineData("  carmen  ")]
    public void Acepta_nombres_de_usuario_validos_y_quita_espacios(string nickname)
    {
        Assert.Equal(nickname.Trim(), FieldLimits.NormalizeNickname(nickname));
    }

    [Fact]
    public void Acepta_nombre_de_usuario_de_exactamente_30_caracteres()
    {
        var nickname = new string('a', FieldLimits.NicknameMaxLength);

        Assert.Equal(nickname, FieldLimits.NormalizeNickname(nickname));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void Rechaza_nombres_de_usuario_vacios_o_muy_cortos(string? nickname)
    {
        Assert.Throws<ArgumentException>(() => FieldLimits.NormalizeNickname(nickname));
    }

    [Fact]
    public void Rechaza_nombre_de_usuario_de_31_caracteres_con_mensaje_claro()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => FieldLimits.NormalizeNickname(new string('a', FieldLimits.NicknameMaxLength + 1)));

        Assert.Contains("entre 3 y 30", ex.Message);
    }

    [Fact]
    public void Rechaza_correo_de_mas_de_254_caracteres()
    {
        Assert.Throws<ArgumentException>(
            () => FieldLimits.EnsureEmailLength(new string('a', FieldLimits.EmailMaxLength + 1)));

        FieldLimits.EnsureEmailLength(new string('a', FieldLimits.EmailMaxLength));
    }

    [Fact]
    public void Rechaza_nombre_de_archivo_de_mas_de_255_caracteres()
    {
        Assert.Throws<ArgumentException>(
            () => FieldLimits.EnsureFileNameLength(new string('a', FieldLimits.FileNameMaxLength + 1)));

        FieldLimits.EnsureFileNameLength(new string('a', FieldLimits.FileNameMaxLength));
    }

    [Fact]
    public void Telefono_con_muchos_separadores_no_supera_el_limite_de_la_base()
    {
        // 15 dígitos con un guion entre cada uno: pasa el formato y los dígitos, pero mide 29.
        Assert.Throws<ArgumentException>(() => PhonePolicy.Normalize("1-2-3-4-5-6-7-8-9-1-2-3-4-5-6"));
    }
}
