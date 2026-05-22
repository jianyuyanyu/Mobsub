using Mobsub.SubtitleProcess;
using Mobsub.SubtitleParse.AssTypes;
using Mobsub.SubtitleParse.PGS;
using System.CommandLine;

namespace Mobsub.Ikkoku.CommandLine;

internal partial class ConvertCmd
{
    internal static Command Build(Argument<FileSystemInfo> path, Option<FileSystemInfo> optPath)
    {
        var inputSuffix = new Option<string>("--from-format") { Description = "Format which will convert from. Use image/.image for all supported image files in a directory." };
        var convertSuffix = new Option<string>("--to-format") { Description = "Format which will convert to", Required = true };
        var imageBinarizeThreshold = new Option<byte?>("--image-binarize-threshold")
        {
            Description = "Image Binarize Threshold when input is .sup or image, range is 0-255, 0 is disabled. Default: 0 (convert to .bmp) / 128 (convert to .txt)",
            DefaultValueFactory = _ => null
        };

        inputSuffix.Validators.Add(result =>
        {
            var p = result.GetValue(path);
            var s = result.GetValue(inputSuffix);
            if (p is DirectoryInfo && s is null)
            {
                result.AddError("You should specify --from-format when input is a directory.");
            }
        });

        var convSubtitleCommand = new Command("convert", "Convert subtitle format")
        {
            path, optPath, convertSuffix, inputSuffix, imageBinarizeThreshold
        };

        var ocrOptions = new OcrCommandOptions();
        AddOptionalOcrOptions(convSubtitleCommand, ocrOptions);

        convSubtitleCommand.SetAction(result =>
        {
            ApplyOptionalOcrEngine(result, ocrOptions);
            Execute(
                result.GetValue(path)!,
                result.GetValue(optPath),
                result.GetValue(convertSuffix)!,
                result.GetValue(inputSuffix)!,
                result.GetValue(imageBinarizeThreshold),
                ocrOptions.SelectedEngine,
                ocrOptions.RecognitionMode);
        });

        return convSubtitleCommand;
    }

    internal static void Execute(
        FileSystemInfo path,
        FileSystemInfo? optPath,
        string convertSuffix,
        string inputSuffix,
        byte? imageBinarizeThreshold,
        ImageSubtitleOcrEngine ocrEngine = ImageSubtitleOcrEngine.OneOcr,
        ImageSubtitleOcrRecognitionMode recognitionMode = ImageSubtitleOcrRecognitionMode.Auto)
    {
        switch (path)
        {
            case FileInfo f:
                ConvertSubtitle(f, optPath, convertSuffix, imageBinarizeThreshold, ocrEngine, recognitionMode);
                break;
            case DirectoryInfo d:
                if (!convertSuffix.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var f in Utils.Traversal(d, inputSuffix))
                        ConvertSubtitle(f, optPath, convertSuffix, imageBinarizeThreshold, ocrEngine, recognitionMode);
                    break;
                }

                imageBinarizeThreshold ??= 128;

                if (ImageSubtitleOcr.IsSupportedImageExtension(inputSuffix))
                {
                    var imageFiles = ImageSubtitleOcr.GetImageFiles(d, inputSuffix);
                    var optFile = ResolveOutputFileForDir(d, optPath, ".txt");
                    using var ocr = new ImageSubtitleOcr(ocrEngine, recognitionMode);
                    ocr.OcrImages(imageFiles, optFile.FullName, (byte)imageBinarizeThreshold);
                    break;
                }

                var files = Utils.Traversal(d, inputSuffix);

                if (inputSuffix.Equals(".sup", StringComparison.OrdinalIgnoreCase))
                {
                    using var ocr = new ImageSubtitleOcr(ocrEngine, recognitionMode);
                    foreach (var f in files)
                    {
                        var optFile = ResolveOutputFile(f, optPath, ".txt");
                        ocr.OcrPgsSup(f.FullName, optFile.FullName, (byte)imageBinarizeThreshold);
                    }
                }
                else
                {
                    foreach (var f in files)
                        ConvertSubtitle(f, optPath, convertSuffix, imageBinarizeThreshold, ocrEngine, recognitionMode);
                }
                break;
        }
    }

    internal static void ConvertSubtitle(
        FileInfo fromFile,
        FileSystemInfo? optPath,
        string convertSuffix,
        byte? imageBinarizeThreshold,
        ImageSubtitleOcrEngine ocrEngine = ImageSubtitleOcrEngine.OneOcr,
        ImageSubtitleOcrRecognitionMode recognitionMode = ImageSubtitleOcrRecognitionMode.Auto)
    {
        if (fromFile.Extension.Equals(convertSuffix, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"{convertSuffix} can’t same as {fromFile.Extension}");

        if (ImageSubtitleOcr.IsSupportedImageExtension(fromFile.Extension))
        {
            imageBinarizeThreshold ??= 128;
            var optFile = ResolveOutputFile(fromFile, optPath, ".txt");
            using var ocr = new ImageSubtitleOcr(ocrEngine, recognitionMode);
            ocr.OcrImage(fromFile.FullName, optFile.FullName, (byte)imageBinarizeThreshold);
            return;
        }

        switch (fromFile.Extension.ToLowerInvariant())
        {
            case ".ass":
                var ass = new AssData();
                ass.ReadAssFile(fromFile.FullName);
                switch (convertSuffix)
                {
                    case ".txt":
                        {
                            var optFile = ResolveOutputFile(fromFile, optPath, convertSuffix);
                            using var fs = new FileStream(optFile.FullName, FileMode.Create, FileAccess.Write);
                            using (var memStream = new MemoryStream())
                            {
                                using var sw = new StreamWriter(memStream, Mobsub.SubtitleParse.Utils.EncodingRefOS());
                                ConvertSub.ConvertAssToTxt(sw, ass);
                                sw.Flush();
                                memStream.Seek(0, SeekOrigin.Begin);
                                memStream.CopyTo(fs);
                            }
                            break;
                        }
                    default:
                        throw new NotImplementedException($"Unsupported: {fromFile.Extension} convert to {convertSuffix}.");
                }
                break;
            case ".sup":
                switch (convertSuffix)
                {
                    case ".bmp":
                        imageBinarizeThreshold ??= 0;
                        var optDir = ResolveOutputDirectory(fromFile, optPath);
                        PGSData.DecodeImages(fromFile.FullName, optDir.FullName, (byte)imageBinarizeThreshold);
                        break;
                    case ".txt":
                        {
                            imageBinarizeThreshold ??= 128;
                            var optSupFile = ResolveOutputFile(fromFile, optPath, convertSuffix);
                            using var ocr = new ImageSubtitleOcr(ocrEngine, recognitionMode);
                            ocr.OcrPgsSup(fromFile.FullName, optSupFile.FullName, (byte)imageBinarizeThreshold);
                            break;
                        }
                    default:
                        throw new NotImplementedException($"Unsupported: {fromFile.Extension} convert to {convertSuffix}.");
                }
                break;
            default:
                throw new NotImplementedException($"Unsupported: {fromFile.Extension}.");
        }
    }

    private static FileInfo ResolveOutputFile(FileInfo fromFile, FileSystemInfo? optPath, string convertSuffix)
        => optPath is FileInfo f ? f : Utils.ChangeSuffix(fromFile, ResolveOutputDirectory(fromFile, optPath), convertSuffix);

    private static DirectoryInfo ResolveOutputDirectory(FileInfo fromFile, FileSystemInfo? optPath)
    {
        return optPath switch
        {
            DirectoryInfo d => d,
            FileInfo f => f.Directory!,
            _ => fromFile.Directory!,
        };
    }

    private static FileInfo ResolveOutputFileForDir(DirectoryInfo fromDir, FileSystemInfo? optPath, string convertSuffix)
    {
        return optPath switch
        {
            FileInfo f => f,
            DirectoryInfo d => new FileInfo(Path.Combine(d.FullName, fromDir.Name + convertSuffix)),
            _ => new FileInfo(Path.Combine(fromDir.FullName, fromDir.Name + convertSuffix)),
        };
    }

    private sealed class OcrCommandOptions
    {
        public ImageSubtitleOcrEngine SelectedEngine { get; set; } = ImageSubtitleOcrEngine.OneOcr;

        public ImageSubtitleOcrRecognitionMode RecognitionMode { get; set; } = ImageSubtitleOcrRecognitionMode.Auto;

        public Option<string>? OcrEngine { get; set; }

        public Option<string>? OneOcrSharpRecognitionMode { get; set; }
    }

    static partial void AddOptionalOcrOptions(Command command, OcrCommandOptions options);

    static partial void ApplyOptionalOcrEngine(ParseResult result, OcrCommandOptions options);
}
