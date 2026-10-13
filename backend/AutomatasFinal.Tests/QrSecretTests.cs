using AutomatasFinal.Infrastructure.Credentials;

namespace AutomatasFinal.Tests;

public class QrSecretTests
{
    [Fact]
    public void El_hash_coincide_con_el_vector_conocido_de_SHA256()
    {
        Assert.Equal(
            "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            CredentialService.HashQrSecret("abc"));
    }

    [Fact]
    public void El_hash_es_determinista_y_distinto_por_secreto()
    {
        var a1 = CredentialService.HashQrSecret("secreto-a");
        var a2 = CredentialService.HashQrSecret("secreto-a");
        var b = CredentialService.HashQrSecret("secreto-b");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public void El_hash_no_contiene_el_secreto()
    {
        const string secret = "kJ3xSecretoDePrueba";
        var hash = CredentialService.HashQrSecret(secret);

        Assert.DoesNotContain(secret, hash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", hash);
    }
}
