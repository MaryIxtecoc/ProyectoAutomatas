using AutomatasFinal.Infrastructure.Biometrics;
using OpenCvSharp;

namespace AutomatasFinal.Tests;

/// <summary>
/// Reconocimiento facial con los modelos reales (YuNet + SFace) y fotos de prueba.
/// Comparte una sola instancia del servicio (cargar los modelos es costoso).
/// </summary>
public class FaceRecognitionTests : IClassFixture<FaceRecognitionTests.ServiceFixture>
{
    public class ServiceFixture : IDisposable
    {
        public FaceRecognitionService Service { get; } = new();
        public void Dispose() => Service.Dispose();
    }

    private readonly FaceRecognitionService _service;

    public FaceRecognitionTests(ServiceFixture fixture)
    {
        _service = fixture.Service;
    }

    private static byte[] Photo(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private float[] Descriptor(string name)
    {
        var result = _service.AnalyzeFace(Photo(name));
        Assert.True(result.FaceDetected);
        Assert.False(result.MultipleFaces);
        return result.Descriptor;
    }

    [Fact]
    public void Genera_un_descriptor_valido_de_128_valores()
    {
        var descriptor = Descriptor("persona1.png");

        Assert.Equal(FaceRecognitionService.DescriptorLength, descriptor.Length);
        Assert.True(FaceRecognitionService.IsValidDescriptor(descriptor));
    }

    [Fact]
    public void Misma_persona_con_otra_codificacion_supera_el_umbral()
    {
        var similarity = _service.Compare(Descriptor("persona1.png"), Descriptor("persona1_otra_codificacion.webp"));

        Assert.True(similarity >= BiometricService.FaceMatchThreshold, $"similitud {similarity}");
    }

    [Theory]
    [InlineData("persona2.png")]
    [InlineData("persona3.png")]
    public void Otra_persona_queda_por_debajo_del_umbral(string otherPerson)
    {
        var similarity = _service.Compare(Descriptor("persona1.png"), Descriptor(otherPerson));

        Assert.True(similarity < BiometricService.FaceMatchThreshold, $"similitud {similarity}");
    }

    [Fact]
    public void Imagen_sin_rostro_no_genera_descriptor()
    {
        using var blank = new Mat(480, 640, MatType.CV_8UC3, new Scalar(120, 160, 90));
        Cv2.ImEncode(".jpg", blank, out var bytes);

        var result = _service.AnalyzeFace(bytes);

        Assert.False(result.FaceDetected);
        Assert.Empty(result.Descriptor);
    }

    [Fact]
    public void Imagen_con_dos_rostros_se_marca_como_multiple()
    {
        using var a = Cv2.ImDecode(Photo("persona1.png"), ImreadModes.Color);
        using var b = Cv2.ImDecode(Photo("persona2.png"), ImreadModes.Color);
        using var ra = new Mat();
        using var rb = new Mat();
        Cv2.Resize(a, ra, new Size(400, 500));
        Cv2.Resize(b, rb, new Size(400, 500));
        using var both = new Mat();
        Cv2.HConcat(new[] { ra, rb }, both);
        Cv2.ImEncode(".jpg", both, out var bytes);

        var result = _service.AnalyzeFace(bytes);

        Assert.True(result.MultipleFaces);
        Assert.Equal(2, result.FaceCount);
    }

    [Fact]
    public void Imagen_corrupta_lanza_error_controlado()
    {
        Assert.Throws<ArgumentException>(() => _service.AnalyzeFace(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Descriptores_invalidos_se_rechazan()
    {
        Assert.False(FaceRecognitionService.IsValidDescriptor(null));
        Assert.False(FaceRecognitionService.IsValidDescriptor(new float[64]));
        Assert.False(FaceRecognitionService.IsValidDescriptor(new float[128]));
        Assert.False(FaceRecognitionService.IsValidDescriptor(Enumerable.Repeat(float.NaN, 128).ToArray()));

        Assert.Throws<ArgumentException>(() => _service.Compare(new float[128], Descriptor("persona1.png")));
    }
}
