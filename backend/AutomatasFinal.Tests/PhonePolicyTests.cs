using AutomatasFinal.Application.Security;

namespace AutomatasFinal.Tests;

public class PhonePolicyTests
{
    [Theory]
    [InlineData("5555-5555")]
    [InlineData("55555555")]
    [InlineData("+502 5555 5555")]
    [InlineData("+50255555555")]
    public void Acepta_telefonos_solo_con_numeros(string phone)
    {
        var result = PhonePolicy.Normalize(phone);
        Assert.Equal(phone, result);
    }

    [Fact]
    public void Quita_espacios_sobrantes()
    {
        Assert.Equal("+502 5555 5555", PhonePolicy.Normalize("  +502   5555 5555  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("5555abcd")]
    [InlineData("12a45678")]
    [InlineData("tel: 55555555")]
    [InlineData("5555-5555 ext")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("555")]
    [InlineData("1234567890123456")]
    [InlineData("55--5555")]
    [InlineData("5555+5555")]
    public void Rechaza_telefonos_invalidos(string? phone)
    {
        Assert.Throws<ArgumentException>(() => PhonePolicy.Normalize(phone));
    }
}
