using SkiaSharp;

namespace MorganHacks.Directory.Images;

/// <summary>Turns untrusted uploads into one metadata-free, square WebP.</summary>
public static class DirectoryPhotoProcessor
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int OutputSize = 400;
    private static readonly SemaphoreSlim Decoders = new(2);
    private const string InvalidPhoto = "Choose a valid JPEG, PNG or WebP up to 5 MB, with each side between 200 and 8000 pixels.";

    public static async Task<byte[]> ProcessAsync(byte[] bytes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var jpeg = bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        var png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var webp = bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (bytes.Length > MaxBytes || (!jpeg && !png && !webp)) throw new InvalidDataException(InvalidPhoto);

        await Decoders.WaitAsync(ct);
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
                throw new InvalidDataException(InvalidPhoto);
            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width is < 200 or > 8000 || height is < 200 or > 8000) throw new InvalidDataException(InvalidPhoto);
            ct.ThrowIfCancellationRequested();

            // Inspect dimensions before allocating pixels. Decode only frame zero;
            // incomplete input is rejected rather than publishing partially decoded pixels.
            // https://github.com/mono/SkiaSharp/blob/v4.153.1/binding/SkiaSharp/SKCodec.cs
            using var srgb = SKColorSpace.CreateSrgb();
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
            using var decoded = new SKBitmap(info);
            if (codec.GetPixels(info, decoded.GetPixels(), decoded.RowBytes, new SKCodecOptions(0)) != SKCodecResult.Success)
                throw new InvalidDataException(InvalidPhoto);
            ct.ThrowIfCancellationRequested();

            using var output = new SKBitmap(new SKImageInfo(OutputSize, OutputSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(output))
            using (var image = SKImage.FromBitmap(decoded))
            {
                canvas.Clear(SKColors.Transparent);
                var swapped = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                var orientedWidth = swapped ? height : width;
                var orientedHeight = swapped ? width : height;
                var scale = Math.Max((float)OutputSize / orientedWidth, (float)OutputSize / orientedHeight);
                canvas.Translate(OutputSize / 2f, OutputSize / 2f);
                canvas.Scale(scale);
                canvas.Translate(-orientedWidth / 2f, -orientedHeight / 2f);
                canvas.Concat(Orientation(codec.EncodedOrigin, width, height));
                canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                canvas.Flush();
            }
            ct.ThrowIfCancellationRequested();
            // Encode fresh pixels, not the source container: EXIF/GPS, XMP, IPTC,
            // ICC and animation chunks cannot be copied into the published file.
            using var pixels = output.PeekPixels();
            using var encoded = pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, 82));
            if (encoded is null) throw new InvalidOperationException("Photo encoding failed.");
            ct.ThrowIfCancellationRequested();
            return encoded.ToArray();
        }
        finally { Decoders.Release(); }
    }

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}
