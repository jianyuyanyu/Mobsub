using Mobsub.SubtitleProcess;
using System.CommandLine;

namespace Mobsub.Ikkoku.CommandLine;

internal partial class ConvertCmd
{
    static partial void AddOptionalOcrOptions(Command command, OcrCommandOptions options)
    {
        var ocrEngine = new Option<string>("--ocr-engine")
        {
            Description = "OCR engine for .sup and image to .txt. Values: oneocr, oneocrsharp.",
            DefaultValueFactory = _ => "oneocr"
        };
        ocrEngine.Validators.Add(result =>
        {
            var engine = result.GetValue(ocrEngine);
            if (!IsValidOcrEngine(engine))
                result.AddError("You should specify --ocr-engine as oneocr or oneocrsharp.");
        });

        var recognitionMode = new Option<string>("--oneocrsharp-recognition-mode")
        {
            Description = "OneOcrSharp recognition mode. Values: auto, cjk.",
            DefaultValueFactory = _ => "auto"
        };
        recognitionMode.Validators.Add(result =>
        {
            var mode = result.GetValue(recognitionMode);
            if (!IsValidRecognitionMode(mode))
                result.AddError("You should specify --oneocrsharp-recognition-mode as auto or cjk.");
        });

        command.Options.Add(ocrEngine);
        command.Options.Add(recognitionMode);
        options.OcrEngine = ocrEngine;
        options.OneOcrSharpRecognitionMode = recognitionMode;
    }

    static partial void ApplyOptionalOcrEngine(ParseResult result, OcrCommandOptions options)
    {
        options.SelectedEngine = ParseOcrEngine(options.OcrEngine is null ? null : result.GetValue(options.OcrEngine));
        options.RecognitionMode = ParseRecognitionMode(
            options.OneOcrSharpRecognitionMode is null ? null : result.GetValue(options.OneOcrSharpRecognitionMode));
    }

    private static bool IsValidOcrEngine(string? value)
        => value is null ||
           value.Equals("oneocr", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("oneocrsharp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidRecognitionMode(string? value)
        => value is null ||
           value.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("cjk", StringComparison.OrdinalIgnoreCase);

    private static ImageSubtitleOcrEngine ParseOcrEngine(string? value)
    {
        if (value is null || value.Equals("oneocr", StringComparison.OrdinalIgnoreCase))
            return ImageSubtitleOcrEngine.OneOcr;

        if (value.Equals("oneocrsharp", StringComparison.OrdinalIgnoreCase))
            return ImageSubtitleOcrEngine.OneOcrSharp;

        throw new ArgumentException($"Unsupported OCR engine: {value}", nameof(value));
    }

    private static ImageSubtitleOcrRecognitionMode ParseRecognitionMode(string? value)
    {
        if (value is null || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return ImageSubtitleOcrRecognitionMode.Auto;

        if (value.Equals("cjk", StringComparison.OrdinalIgnoreCase))
            return ImageSubtitleOcrRecognitionMode.Cjk;

        throw new ArgumentException($"Unsupported OneOcrSharp recognition mode: {value}", nameof(value));
    }
}
