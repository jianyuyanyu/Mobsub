using Mobsub.Helper;
using OneOcrSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mobsub.SubtitleProcess;

public sealed partial class ImageSubtitleOcr
{
    private OneOcrOnnxPipeline? oneOcrSharpPipeline;

    partial void InitializeOptionalOcrEngine(ImageSubtitleOcrEngine ocrEngine, ref bool handled)
    {
        if (ocrEngine != ImageSubtitleOcrEngine.OneOcrSharp)
            return;

        oneOcrSharpPipeline = new OneOcrOnnxPipeline(new OneOcrOnnxPipelineOptions
        {
            RecognitionMode = ToOneOcrRecognitionMode(recognitionMode),
            EnableQualityScoring = false,
        });
        handled = true;
    }

    partial void DisposeOptionalOcrEngine()
    {
        oneOcrSharpPipeline?.Dispose();
    }

    partial void OcrOptionalImage(string imageFile, TextWriter writer, byte imageBinarizeThreshold, ref bool handled)
    {
        if (ocrEngineType != ImageSubtitleOcrEngine.OneOcrSharp)
            return;

        using var image = LoadImage(imageFile, imageBinarizeThreshold);
        OcrOneOcrSharpImage(image, writer);
        handled = true;
    }

    partial void OcrOptionalBitmap(SimpleBitmap bitmap, TextWriter writer, ref bool handled)
    {
        if (ocrEngineType != ImageSubtitleOcrEngine.OneOcrSharp)
            return;

        using var image = ToImage(bitmap);
        OcrOneOcrSharpImage(image, writer);
        handled = true;
    }

    private static Image<Rgb24> LoadImage(string imageFile, byte imageBinarizeThreshold)
    {
        var image = Image.Load<Rgb24>(imageFile);
        if (imageBinarizeThreshold > 0)
            Binarize(image, imageBinarizeThreshold);

        return image;
    }

    private void OcrOneOcrSharpImage(Image<Rgb24> image, TextWriter writer)
    {
        ProcessImage(image);
        WriteOneOcrSharpResult(oneOcrSharpPipeline!.RunImage(image), writer);
    }

    private static void WriteOneOcrSharpResult(IReadOnlyList<OcrLine>? result, TextWriter writer)
    {
        if (result is null || result.Count == 0) return;
        if (result.Count == 1)
        {
            writer.WriteLine(result[0].Text);
            return;
        }

        var wroteLine = false;
        for (var i = 0; i < result.Count; i++)
        {
            var line = result[i];
            if (line.Y3 - line.Y1 < 30) continue;

            if (wroteLine)
                writer.Write("\\N");

            writer.Write(line.Text);
            wroteLine = true;
        }

        if (wroteLine)
            writer.WriteLine();
    }

    private static void Binarize(Image<Rgb24> image, byte threshold)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var gray = (pixel.R * 77 + pixel.G * 150 + pixel.B * 29) >> 8;
                    var binary = (byte)(gray > threshold ? 255 : 0);
                    row[x] = new Rgb24(binary, binary, binary);
                }
            }
        });
    }

    private static Image<Rgb24> ToImage(SimpleBitmap bitmap)
    {
        var image = new Image<Rgb24>(bitmap.GetWidth(), bitmap.GetHeight());
        image.ProcessPixelRows(accessor =>
        {
            var source = bitmap.GetPixelSpan();
            var width = bitmap.GetWidth();
            var stride = bitmap.GetStride();

            for (var y = 0; y < accessor.Height; y++)
            {
                var sourceRow = source.Slice(y * stride, width * 4);
                var targetRow = accessor.GetRowSpan(y);

                for (var x = 0; x < targetRow.Length; x++)
                {
                    var sourceIndex = x * 4;
                    targetRow[x] = new Rgb24(
                        sourceRow[sourceIndex + 2],
                        sourceRow[sourceIndex + 1],
                        sourceRow[sourceIndex]);
                }
            }
        });

        return image;
    }

    private static void ProcessImage(Image<Rgb24> image)
    {
        if (image.Width >= 50 && image.Height >= 50) return;

        var scale = image.Width >= 25 && image.Height >= 25 ? 2 : 4;
        image.Mutate(context => context.Resize(image.Width * scale, image.Height * scale, KnownResamplers.NearestNeighbor));
    }

    private static OneOcrRecognitionMode ToOneOcrRecognitionMode(ImageSubtitleOcrRecognitionMode mode)
        => mode switch
        {
            ImageSubtitleOcrRecognitionMode.Auto => OneOcrRecognitionMode.Auto,
            ImageSubtitleOcrRecognitionMode.Cjk => OneOcrRecognitionMode.Cjk,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
}
