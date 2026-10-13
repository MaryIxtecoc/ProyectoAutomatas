using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace AutomatasFinal.Infrastructure.Biometrics;

/// <summary>
/// Detección (YuNet) + identidad (SFace) facial.
///
/// OpenCvSharp4 4.13.0.20260627 expone FaceDetectorYN (solo Create/Detect,
/// sin SetInputSize) pero NO expone FaceRecognizerSF. Por eso el paso de
/// SFace se hace a mano con CvDnn, replicando exactamente lo que hace
/// cv::FaceRecognizerSF en OpenCV (alignCrop + feature):
///   - alineación por similitud (mínimos cuadrados) de los 5 landmarks a la
///     plantilla de 112x112,
///   - blob con píxeles en rango 0..255 (escala 1.0, NO 1/255), swapRB=true.
///
/// Se registra como singleton: la red SFace se carga una sola vez. cv::dnn::Net
/// no es seguro para hilos, así que SetInput/Forward van bajo lock.
/// </summary>
public class FaceRecognitionService : IDisposable
{
    /// <summary>Dimensión del descriptor que produce SFace.</summary>
    public const int DescriptorLength = 128;

    /// <summary>Lado mínimo (px) del rostro detectado para aceptarlo.</summary>
    public const float MinFaceSize = 80f;

    private const float DetectionScoreThreshold = 0.9f;
    private const float DetectionNmsThreshold = 0.3f;
    private const int DetectionTopK = 5000;

    private static readonly Size AlignedFaceSize = new(112, 112);

    // Plantilla de landmarks de SFace (ojo der., ojo izq., nariz,
    // comisura der., comisura izq.) sobre la imagen alineada de 112x112.
    private static readonly Point2f[] ReferenceLandmarks =
    {
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f)
    };

    private readonly string _detectorModelPath;
    private readonly string _recognizerModelPath;

    private readonly Net _recognizer;
    private readonly object _recognizerLock = new();

    private bool _disposed;

    public FaceRecognitionService()
    {
        var basePath = AppContext.BaseDirectory;

        _detectorModelPath = Path.Combine(
            basePath,
            "Models",
            "Face",
            "face_detection_yunet_2023mar.onnx");

        _recognizerModelPath = Path.Combine(
            basePath,
            "Models",
            "Face",
            "face_recognition_sface_2021dec.onnx");

        ValidateModels();

        _recognizer = CvDnn.ReadNetFromOnnx(_recognizerModelPath)
            ?? throw new InvalidOperationException(
                "No fue posible cargar el modelo facial SFace.");
    }

    public FaceRecognitionResult AnalyzeFace(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException(
                "La imagen facial es obligatoria.");
        }

        using var image = Cv2.ImDecode(
            imageBytes,
            ImreadModes.Color);

        if (image.Empty())
        {
            throw new ArgumentException(
                "No fue posible leer la imagen.");
        }

        // Esta versión de FaceDetectorYN no tiene SetInputSize, así que el
        // detector se crea con el tamaño de cada imagen. YuNet pesa ~230 KB,
        // crearlo por request es barato (lo caro es SFace, que sí se reutiliza).
        using var detector = FaceDetectorYN.Create(
            _detectorModelPath,
            string.Empty,
            new Size(
                image.Width,
                image.Height),
            DetectionScoreThreshold,
            DetectionNmsThreshold,
            DetectionTopK,
            Backend.DEFAULT,
            Target.CPU);

        using var faces = new Mat();

        detector.Detect(
            image,
            faces);

        if (faces.Empty() || faces.Rows == 0)
        {
            return new FaceRecognitionResult
            {
                FaceDetected = false,
                FaceCount = 0
            };
        }

        if (faces.Rows > 1)
        {
            return new FaceRecognitionResult
            {
                FaceDetected = true,
                FaceCount = faces.Rows,
                MultipleFaces = true
            };
        }

        using var face = faces.Row(0);

        var result = new FaceRecognitionResult
        {
            FaceDetected = true,
            FaceCount = 1,
            MultipleFaces = false,

            X = face.At<float>(0, 0),
            Y = face.At<float>(0, 1),
            Width = face.At<float>(0, 2),
            Height = face.At<float>(0, 3),

            LeftEyeX = face.At<float>(0, 4),
            LeftEyeY = face.At<float>(0, 5),

            RightEyeX = face.At<float>(0, 6),
            RightEyeY = face.At<float>(0, 7),

            NoseX = face.At<float>(0, 8),
            NoseY = face.At<float>(0, 9),

            LeftMouthX = face.At<float>(0, 10),
            LeftMouthY = face.At<float>(0, 11),

            RightMouthX = face.At<float>(0, 12),
            RightMouthY = face.At<float>(0, 13),

            Confidence = face.At<float>(0, 14)
        };

        if (result.Width < MinFaceSize ||
            result.Height < MinFaceSize)
        {
            // Rostro detectado pero demasiado pequeño para una identidad
            // confiable: no se genera descriptor.
            result.FaceTooSmall = true;
            return result;
        }

        result.Descriptor = GenerateDescriptor(
            image,
            result);

        return result;
    }

    /// <summary>
    /// Similitud coseno entre dos descriptores SFace (rango -1..1).
    /// No modifica los arreglos recibidos.
    /// </summary>
    public double Compare(
        float[] descriptor1,
        float[] descriptor2)
    {
        if (!IsValidDescriptor(descriptor1) ||
            !IsValidDescriptor(descriptor2))
        {
            throw new ArgumentException(
                "Descriptor facial inválido.");
        }

        double dot = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (var i = 0; i < descriptor1.Length; i++)
        {
            dot += (double)descriptor1[i] * descriptor2[i];
            norm1 += (double)descriptor1[i] * descriptor1[i];
            norm2 += (double)descriptor2[i] * descriptor2[i];
        }

        return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    /// <summary>
    /// Un descriptor válido tiene exactamente 128 valores finitos y norma no nula.
    /// </summary>
    public static bool IsValidDescriptor(
        float[]? descriptor)
    {
        if (descriptor is null ||
            descriptor.Length != DescriptorLength)
        {
            return false;
        }

        double norm = 0;

        foreach (var value in descriptor)
        {
            if (!float.IsFinite(value))
            {
                return false;
            }

            norm += (double)value * value;
        }

        return norm > 1e-12;
    }

    private float[] GenerateDescriptor(
        Mat image,
        FaceRecognitionResult face)
    {
        using var alignedFace = AlignCrop(
            image,
            face);

        // Igual que cv::FaceRecognizerSF::feature: escala 1.0, sin media, BGR→RGB.
        using var blob = CvDnn.BlobFromImage(
            alignedFace,
            1.0,
            AlignedFaceSize,
            new Scalar(0, 0, 0),
            swapRB: true,
            crop: false);

        float[] descriptor;

        lock (_recognizerLock)
        {
            _recognizer.SetInput(blob);

            using var feature = _recognizer.Forward();

            descriptor = new float[feature.Total()];
            feature.GetArray(out descriptor);
        }

        Normalize(descriptor);

        if (!IsValidDescriptor(descriptor))
        {
            throw new InvalidOperationException(
                "No fue posible generar el descriptor facial.");
        }

        return descriptor;
    }

    private static Mat AlignCrop(
        Mat image,
        FaceRecognitionResult face)
    {
        var landmarks = new[]
        {
            new Point2f(face.LeftEyeX, face.LeftEyeY),
            new Point2f(face.RightEyeX, face.RightEyeY),
            new Point2f(face.NoseX, face.NoseY),
            new Point2f(face.LeftMouthX, face.LeftMouthY),
            new Point2f(face.RightMouthX, face.RightMouthY)
        };

        using var transform = EstimateSimilarityTransform(
            landmarks,
            ReferenceLandmarks);

        var aligned = new Mat();

        Cv2.WarpAffine(
            image,
            aligned,
            transform,
            AlignedFaceSize);

        return aligned;
    }

    /// <summary>
    /// Transformación de similitud (rotación + escala uniforme + traslación)
    /// por mínimos cuadrados sobre los 5 puntos, sin reflexión. Equivale a la
    /// que usa OpenCV en alignCrop. Se usan los 5 puntos siempre (con RANSAC
    /// y solo 5 puntos se podían descartar landmarks y deformar la alineación).
    /// </summary>
    private static Mat EstimateSimilarityTransform(
        Point2f[] source,
        Point2f[] destination)
    {
        var count = source.Length;

        double srcMeanX = 0, srcMeanY = 0, dstMeanX = 0, dstMeanY = 0;

        for (var i = 0; i < count; i++)
        {
            srcMeanX += source[i].X;
            srcMeanY += source[i].Y;
            dstMeanX += destination[i].X;
            dstMeanY += destination[i].Y;
        }

        srcMeanX /= count;
        srcMeanY /= count;
        dstMeanX /= count;
        dstMeanY /= count;

        double sumCos = 0, sumSin = 0, sumSquares = 0;

        for (var i = 0; i < count; i++)
        {
            var x = source[i].X - srcMeanX;
            var y = source[i].Y - srcMeanY;
            var u = destination[i].X - dstMeanX;
            var v = destination[i].Y - dstMeanY;

            sumCos += x * u + y * v;
            sumSin += x * v - y * u;
            sumSquares += x * x + y * y;
        }

        if (sumSquares < 1e-6)
        {
            throw new InvalidOperationException(
                "No fue posible calcular la alineación facial.");
        }

        // Matriz [a -b tx; b a ty]
        var a = sumCos / sumSquares;
        var b = sumSin / sumSquares;

        var transform = new Mat(2, 3, MatType.CV_64FC1);

        transform.Set(0, 0, a);
        transform.Set(0, 1, -b);
        transform.Set(0, 2, dstMeanX - (a * srcMeanX - b * srcMeanY));
        transform.Set(1, 0, b);
        transform.Set(1, 1, a);
        transform.Set(1, 2, dstMeanY - (b * srcMeanX + a * srcMeanY));

        return transform;
    }

    public byte[] CropToFace(
        byte[] imageBytes,
        FaceRecognitionResult faceResult,
        double padding = 0.6)
    {
        using var image = Cv2.ImDecode(
            imageBytes,
            ImreadModes.Color);

        if (image.Empty())
        {
            throw new ArgumentException(
                "No fue posible leer la imagen.");
        }

        var padX = faceResult.Width * padding;
        var padY = faceResult.Height * padding;

        var x1 = (int)Math.Max(
            0,
            faceResult.X - padX);

        var y1 = (int)Math.Max(
            0,
            faceResult.Y - padY);

        var x2 = (int)Math.Min(
            image.Width,
            faceResult.X +
            faceResult.Width +
            padX);

        var y2 = (int)Math.Min(
            image.Height,
            faceResult.Y +
            faceResult.Height +
            padY);

        var width = x2 - x1;
        var height = y2 - y1;
        var side = Math.Max(
            width,
            height);

        var centerX = x1 + width / 2;
        var centerY = y1 + height / 2;

        var squareX1 = Math.Max(
            0,
            centerX - side / 2);

        var squareY1 = Math.Max(
            0,
            centerY - side / 2);

        var squareX2 = Math.Min(
            image.Width,
            squareX1 + side);

        var squareY2 = Math.Min(
            image.Height,
            squareY1 + side);

        squareX1 = Math.Max(
            0,
            squareX2 - side);

        squareY1 = Math.Max(
            0,
            squareY2 - side);

        var rect = new Rect(
            squareX1,
            squareY1,
            squareX2 - squareX1,
            squareY2 - squareY1);

        using var cropped = new Mat(
            image,
            rect);

        Cv2.ImEncode(
            ".jpg",
            cropped,
            out var buffer);

        return buffer;
    }

    private static void Normalize(
        float[] vector)
    {
        double norm = 0;

        for (var i = 0; i < vector.Length; i++)
        {
            norm +=
                vector[i] *
                vector[i];
        }

        norm = Math.Sqrt(norm);

        if (norm < 1e-12)
        {
            throw new InvalidOperationException(
                "No fue posible normalizar el descriptor facial.");
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] =
                (float)(vector[i] / norm);
        }
    }

    private void ValidateModels()
    {
        if (!File.Exists(
                _detectorModelPath))
        {
            throw new FileNotFoundException(
                "No se encontró el modelo facial YuNet.",
                _detectorModelPath);
        }

        if (!File.Exists(
                _recognizerModelPath))
        {
            throw new FileNotFoundException(
                "No se encontró el modelo facial SFace.",
                _recognizerModelPath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _recognizer.Dispose();

        _disposed = true;
    }
}

public class FaceRecognitionResult
{
    public bool FaceDetected { get; set; }

    public int FaceCount { get; set; }

    public bool MultipleFaces { get; set; }

    /// <summary>Hay un rostro, pero es menor a <see cref="FaceRecognitionService.MinFaceSize"/>.</summary>
    public bool FaceTooSmall { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }

    public float LeftEyeX { get; set; }

    public float LeftEyeY { get; set; }

    public float RightEyeX { get; set; }

    public float RightEyeY { get; set; }

    public float NoseX { get; set; }

    public float NoseY { get; set; }

    public float LeftMouthX { get; set; }

    public float LeftMouthY { get; set; }

    public float RightMouthX { get; set; }

    public float RightMouthY { get; set; }

    public float Confidence { get; set; }

    public float[] Descriptor { get; set; }
        = Array.Empty<float>();
}
