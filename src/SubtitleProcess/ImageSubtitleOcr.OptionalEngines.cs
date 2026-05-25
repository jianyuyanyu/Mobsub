using Mobsub.Helper;

namespace Mobsub.SubtitleProcess;

// Dispatches partial method calls to the appropriate optional OCR engine.
// This file is always compiled and handles all optional engines.
public sealed partial class ImageSubtitleOcr
{
    partial void InitializeOptionalOcrEngine(ImageSubtitleOcrEngine ocrEngine, ref bool handled)
    {
        switch (ocrEngine)
        {
#if USE_ONEOCR_SHARP
            case ImageSubtitleOcrEngine.OneOcrSharp:
                InitializeOneOcrSharpEngine(ocrEngine, ref handled);
                break;
#endif
            case ImageSubtitleOcrEngine.MeikiOcr:
                InitializeMeikiOcrEngine(ref handled);
                break;
        }
    }

    partial void DisposeOptionalOcrEngine()
    {
#if USE_ONEOCR_SHARP
        DisposeOneOcrSharpEngine();
#endif
        DisposeMeikiOcrEngine();
    }

    partial void OcrOptionalImage(string imageFile, TextWriter writer, byte imageBinarizeThreshold, ref bool handled)
    {
        switch (ocrEngineType)
        {
#if USE_ONEOCR_SHARP
            case ImageSubtitleOcrEngine.OneOcrSharp:
                OcrOneOcrSharpImageFile(imageFile, writer, imageBinarizeThreshold, ref handled);
                break;
#endif
            case ImageSubtitleOcrEngine.MeikiOcr:
                OcrMeikiOcrImageFile(imageFile, writer, imageBinarizeThreshold, ref handled);
                break;
        }
    }

    partial void OcrOptionalBitmap(SimpleBitmap bitmap, TextWriter writer, ref bool handled)
    {
        switch (ocrEngineType)
        {
#if USE_ONEOCR_SHARP
            case ImageSubtitleOcrEngine.OneOcrSharp:
                OcrOneOcrSharpBitmap(bitmap, writer, ref handled);
                break;
#endif
            case ImageSubtitleOcrEngine.MeikiOcr:
                OcrMeikiOcrBitmap(bitmap, writer, ref handled);
                break;
        }
    }
}
