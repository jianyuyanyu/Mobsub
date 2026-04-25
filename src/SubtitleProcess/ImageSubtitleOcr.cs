using Mobsub.Helper;
using Mobsub.SubtitleParse.PGS;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SnippingToolOcrCore;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Mobsub.SubtitleProcess;

public sealed class ImageSubtitleOcr : IDisposable
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".jpg", ".jpeg", ".png",
    };

    private static readonly Regex NaturalSortRegex = new(@"\d+", RegexOptions.Compiled);

    private readonly Ocr ocrEngine;

    public ImageSubtitleOcr()
    {
        ocrEngine = new Ocr();
        ocrEngine.CreatePipelineAndProcessOptions();
    }

    public void Dispose()
    {
        ocrEngine.Dispose();
    }

    public static bool IsSupportedImageExtension(string extension) => SupportedImageExtensions.Contains(extension);

    public static FileInfo[] GetImageFiles(DirectoryInfo directory, string format)
    {
        return directory.EnumerateFiles()
            .Where(file => file.Extension.Equals(format, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => NaturalSortRegex.Replace(Path.GetFileNameWithoutExtension(f.Name), m => m.Value.PadLeft(10, '0')))
            .ToArray();
    }

    public void OcrPgsSup(string sup, string outputFile, byte imageBinarizeThreshold)
    {
        using var writer = CreateWriter(outputFile);
        foreach (var pic in PGSData.DecodeBitmapData(sup, imageBinarizeThreshold))
        {
            if (pic is null) continue;
            using (pic)
            {
                OcrBitmap(pic, writer);
            }
        }
    }

    public void OcrImage(string imageFile, string outputFile, byte imageBinarizeThreshold)
    {
        using var writer = CreateWriter(outputFile);
        OcrImage(imageFile, writer, imageBinarizeThreshold);
    }

    public void OcrImage(string imageFile, TextWriter writer, byte imageBinarizeThreshold)
    {
        using var bitmap = LoadImage(imageFile, imageBinarizeThreshold);
        OcrBitmap(bitmap, writer);
    }

    public void OcrImages(IEnumerable<FileInfo> imageFiles, string outputFile, byte imageBinarizeThreshold)
    {
        using var writer = CreateWriter(outputFile);
        foreach (var f in imageFiles)
            OcrImage(f.FullName, writer, imageBinarizeThreshold);
    }

    private static StreamWriter CreateWriter(string outputFile)
    {
        var dir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        return new StreamWriter(outputFile, false, Encoding.UTF8, 1 << 16);
    }

    private static SimpleBitmap LoadImage(string imageFile, byte imageBinarizeThreshold)
    {
        using var image = Image.Load<Bgra32>(imageFile);
        var bitmap = new SimpleBitmap(image.Width, image.Height);

        image.ProcessPixelRows(accessor =>
        {
            var destination = bitmap.GetPixelSpan().Slice(0, bitmap.GetStride() * bitmap.GetHeight());
            for (var y = 0; y < accessor.Height; y++)
                MemoryMarshal.AsBytes(accessor.GetRowSpan(y)).CopyTo(destination.Slice(y * bitmap.GetStride(), bitmap.GetWidth() * 4));
        });

        if (imageBinarizeThreshold > 0)
            bitmap.BinarizeVector2(imageBinarizeThreshold);

        return bitmap;
    }

    private void OcrBitmap(SimpleBitmap pic, TextWriter writer)
    {
        var newPic = ProcessImage(pic);
        try
        {
            unsafe
            {
                fixed (byte* p = newPic.GetPixelData())
                {
                    var img = new Img()
                    {
                        t = 3,
                        col = newPic.GetWidth(),
                        row = newPic.GetHeight(),
                        _unk = 0,
                        step = newPic.GetStride(),
                        data_ptr = (IntPtr)p
                    };

                    WriteOcrResult(ocrEngine.RunOcr(img), writer);
                }
            }
        }
        finally
        {
            if (!ReferenceEquals(newPic, pic))
                newPic.Dispose();
        }
    }

    private static void WriteOcrResult(Line[]? result, TextWriter writer)
    {
        if (result is null || result.Length == 0) return;
        if (result.Length == 1)
        {
            writer.WriteLine(result[0].Text);
            return;
        }

        var wroteLine = false;
        for (var i = 0; i < result.Length; i++)
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

    private static SimpleBitmap ProcessImage(SimpleBitmap pic)
    {
        if (pic.GetWidth() >= 50 && pic.GetHeight() >= 50) return pic;

        var scale = pic.GetWidth() >= 25 && pic.GetHeight() >= 25 ? 2 : 4;
        return pic.ResizeNearest(scale);
    }
}
