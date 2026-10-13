using AutomatasFinal.Application.Security;

namespace AutomatasFinal.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    public void Rechaza_contrasenas_vacias_o_cortas(string? password)
    {
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(password));
    }

    [Fact]
    public void Rechaza_contrasenas_de_mas_de_128_caracteres()
    {
        Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate(new string('a', 129)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(128)]
    public void Acepta_contrasenas_dentro_del_rango(int length)
    {
        PasswordPolicy.Validate(new string('a', length));
    }

    [Fact]
    public void El_mensaje_usa_el_nombre_del_campo()
    {
        var ex = Assert.Throws<ArgumentException>(() => PasswordPolicy.Validate("123", "La nueva contraseña"));
        Assert.StartsWith("La nueva contraseña", ex.Message);
    }
}
