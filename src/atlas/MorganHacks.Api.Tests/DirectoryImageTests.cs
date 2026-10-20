using System.Buffers.Binary;
using MorganHacks.Directory.Images;
using SkiaSharp;

namespace MorganHacks.Api.Tests;

public class DirectoryImageTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DirectoryImages", name));

    [Theory]
    [InlineData(1, "RGBY")]
    [InlineData(2, "GRYB")]
    [InlineData(3, "YBGR")]
    [InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")]
    [InlineData(6, "BRYG")]
    [InlineData(7, "YGBR")]
    [InlineData(8, "GYRB")]
    public async Task Honors_all_orientations_before_center_cropping(int orientation, string corners)
    {
        var result = await DirectoryPhotoProcessor.ProcessAsync(Fixture($"orientation-{orientation}.jpg"));
        using var image = SKBitmap.Decode(result);
        Assert.Equal(400, image.Width);
        Assert.Equal(400, image.Height);
        AssertColor(image.GetPixel(50, 50), corners[0]);
        AssertColor(image.GetPixel(350, 50), corners[1]);
        AssertColor(image.GetPixel(50, 350), corners[2]);
        AssertColor(image.GetPixel(350, 350), corners[3]);
        AssertMetadataFreeWebp(result);
    }

    [Theory]
    [InlineData("landscape.png")]
    [InlineData("landscape.webp")]
    public async Task Accepts_png_and_webp_and_reencodes_without_metadata(string name)
    {
        var result = await DirectoryPhotoProcessor.ProcessAsync(Fixture(name));
        using var image = SKBitmap.Decode(result);
        Assert.Equal(400, image.Width);
        Assert.Equal(400, image.Height);
        AssertMetadataFreeWebp(result);
    }

    [Fact]
    public async Task Keeps_transparency()
    {
        using var image = SKBitmap.Decode(await DirectoryPhotoProcessor.ProcessAsync(Fixture("transparent.png")));
        Assert.Equal(0, image.GetPixel(0, 0).Alpha);
        Assert.InRange(image.GetPixel(200, 200).Alpha, (byte)125, (byte)131);
    }

    [Fact]
    public async Task Animated_input_publishes_only_the_first_frame()
    {
        var result = await DirectoryPhotoProcessor.ProcessAsync(Fixture("animated.webp"));
        using var image = SKBitmap.Decode(result);
        AssertColor(image.GetPixel(200, 200), 'R');
        AssertMetadataFreeWebp(result);
    }

    [Theory]
    [InlineData(199, 300)]
    [InlineData(300, 199)]
    [InlineData(8001, 200)]
    [InlineData(200, 8001)]
    public async Task Rejects_out_of_range_dimensions(int width, int height) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryPhotoProcessor.ProcessAsync(DirectoryImageFixtures.Png(width, height)));

    [Theory]
    [InlineData(200, 200)]
    [InlineData(8000, 200)]
    public async Task Accepts_dimension_boundaries(int width, int height)
    {
        using var image = SKBitmap.Decode(await DirectoryPhotoProcessor.ProcessAsync(DirectoryImageFixtures.Png(width, height)));
        Assert.Equal(400, image.Width);
    }

    [Theory]
    [InlineData("%PDF-1.7")]
    [InlineData("GIF89a")]
    [InlineData("<svg/>")]
    [InlineData("")]
    public async Task Rejects_other_formats(string text) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryPhotoProcessor.ProcessAsync(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task Rejects_files_larger_than_five_mebibytes() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryPhotoProcessor.ProcessAsync(new byte[DirectoryPhotoProcessor.MaxBytes + 1]));

    [Theory]
    [InlineData("landscape.png")]
    [InlineData("orientation-1.jpg")]
    [InlineData("landscape.webp")]
    public async Task Rejects_truncated_inputs(string name)
    {
        var bytes = Fixture(name);
        await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryPhotoProcessor.ProcessAsync(bytes[..(bytes.Length / 2)]));
    }

    [Fact]
    public async Task Cancellation_and_invalid_input_do_not_leak_decoder_slots()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DirectoryPhotoProcessor.ProcessAsync(Fixture("landscape.png"), cancelled.Token));
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidDataException>(() => DirectoryPhotoProcessor.ProcessAsync(new byte[] { 255, 216, 255, 0 }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DirectoryPhotoProcessor.ProcessAsync(Fixture("landscape.png"), timeout.Token)));
    }

    private static void AssertColor(SKColor actual, char name)
    {
        var expected = name switch { 'R' => SKColors.Red, 'G' => new SKColor(0, 255, 0), 'B' => SKColors.Blue, _ => SKColors.Yellow };
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, 30);
        Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, 30);
        Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, 30);
    }

    // Inspect the container independently of the decoder: no EXIF/GPS, XMP,
    // ICC or animation chunks may be carried into the output.
    private static void AssertMetadataFreeWebp(byte[] data)
    {
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(data, 8, 4));
        Assert.Equal(data.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)));
        var offset = 12;
        while (offset < data.Length)
        {
            var kind = System.Text.Encoding.ASCII.GetString(data, offset, 4);
            Assert.Contains(kind, new[] { "VP8X", "VP8 ", "VP8L", "ALPH" });
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            offset += 8 + length + (length & 1);
        }
        Assert.Equal(data.Length, offset);
    }
}
