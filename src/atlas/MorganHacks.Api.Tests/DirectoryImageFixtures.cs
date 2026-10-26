using SkiaSharp;
namespace MorganHacks.Api.Tests;

internal static class DirectoryImageFixtures
{
    public static byte[] Png(int width = 600, int height = 300)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var pixels = bitmap.PeekPixels();
        using var data = pixels.Encode(SKPngEncoderOptions.Default);
        return data!.ToArray();
    }
}
