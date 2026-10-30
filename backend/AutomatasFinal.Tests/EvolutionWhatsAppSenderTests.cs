using System.Net;
using System.Text.Json;
using AutomatasFinal.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutomatasFinal.Tests;

/// <summary>
/// Pruebas del sender de Evolution API SIN mandar mensajes reales:
/// se usa un HttpMessageHandler falso que captura la petición.
/// </summary>
public class EvolutionWhatsAppSenderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"error\":\"x\"}") };
        }
    }

    private static (EvolutionWhatsAppSender sender, FakeHandler handler) Build(
        Dictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>
            {
                ["WhatsAppApi:BaseUrl"] = "https://evolution.test/",
                ["WhatsAppApi:ApiKey"] = "CLAVE",
                ["WhatsAppApi:Instance"] = "mi-whatsapp"
            })
            .Build();

        var handler = new FakeHandler();
        var sender = new EvolutionWhatsAppSender(
            new HttpClient(handler), config, NullLogger<EvolutionWhatsAppSender>.Instance);
        return (sender, handler);
    }

    [Theory]
    [InlineData("45113021", "50245113021")]
    [InlineData("4511-3021", "50245113021")]
    [InlineData("+502 4511 3021", "50245113021")]
    [InlineData("+50245113021", "50245113021")]
    [InlineData("+1 555 123 4567", "15551234567")]
    public void Normaliza_el_numero(string input, string expected)
    {
        Assert.Equal(expected, EvolutionWhatsAppSender.NormalizePhone(input));
    }

    [Fact]
    public async Task Texto_usa_sendText_con_apikey()
    {
        var (sender, handler) = Build();

        await sender.SendAsync("4511-3021", "Hola");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://evolution.test/message/sendText/mi-whatsapp", handler.Request.RequestUri!.ToString());
        Assert.Equal("CLAVE", handler.Request.Headers.GetValues("apikey").Single());

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("50245113021", json.RootElement.GetProperty("number").GetString());
        Assert.Equal("Hola", json.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Pdf_usa_sendMedia_con_el_formato_de_Postman()
    {
        var (sender, handler) = Build();

        await sender.SendAsync(
            "50245113021",
            "Aquí tienes el documento de prueba.",
            "https://x.supabase.co/storage/v1/object/sign/credentials/abc/credencial.pdf?token=123");

        Assert.Equal("https://evolution.test/message/sendMedia/mi-whatsapp", handler.Request!.RequestUri!.ToString());

        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("50245113021", root.GetProperty("number").GetString());
        Assert.Equal("document", root.GetProperty("mediatype").GetString());
        Assert.Equal("application/pdf", root.GetProperty("mimetype").GetString());
        Assert.Equal("Aquí tienes el documento de prueba.", root.GetProperty("caption").GetString());
        Assert.StartsWith("https://x.supabase.co/", root.GetProperty("media").GetString());
        Assert.Equal("credencial.pdf", root.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Error_de_la_API_lanza_excepcion()
    {
        var (sender, handler) = Build();
        handler.Status = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("45113021", "Hola"));
    }

    [Fact]
    public async Task Sin_configuracion_no_envia_nada()
    {
        var (sender, handler) = Build(new Dictionary<string, string?>());

        await sender.SendAsync("45113021", "Hola");

        Assert.Null(handler.Request);
    }
}
