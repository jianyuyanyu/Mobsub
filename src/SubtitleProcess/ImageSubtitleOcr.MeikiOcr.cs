using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Mobsub.Helper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mobsub.SubtitleProcess;

public sealed partial class ImageSubtitleOcr
{
    private const float MeikiDetTh = 0.3f;
    private const float MeikiRecTh = 0.3f;
    private const int MeikiDetW = 960;
    private const int MeikiDetH = 544;
    private const int MeikiRecH = 32;
    private const int MeikiRecW = 960;
    private const int MeikiMaxN = 64;

    private InferenceSession? meikiDetSession;
    private InferenceSession? meikiRecSession;

    private void InitializeMeikiOcrEngine(ref bool handled)
    {
        var (detPath, recPath) = ResolveMeikiModelPaths();
        meikiDetSession = new InferenceSession(detPath);
        meikiRecSession = new InferenceSession(recPath);
        handled = true;
    }

    private void DisposeMeikiOcrEngine()
    {
        meikiDetSession?.Dispose();
        meikiRecSession?.Dispose();
    }

    private void OcrMeikiOcrImageFile(string imageFile, TextWriter writer, byte imageBinarizeThreshold, ref bool handled)
    {
        using var image = LoadMeikiImage(imageFile, imageBinarizeThreshold);
        OcrMeikiImage(image, writer);
        handled = true;
    }

    private void OcrMeikiOcrBitmap(SimpleBitmap bitmap, TextWriter writer, ref bool handled)
    {
        using var image = ToMeikiImage(bitmap);
        OcrMeikiImage(image, writer);
        handled = true;
    }

    private static Image<Rgba32> LoadMeikiImage(string imageFile, byte imageBinarizeThreshold)
    {
        var image = Image.Load<Rgba32>(imageFile);
        if (imageBinarizeThreshold > 0)
            BinarizeMeikiImage(image, imageBinarizeThreshold);
        return image;
    }

    private static void BinarizeMeikiImage(Image<Rgba32> image, byte threshold)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    var gray = (pixel.R * 77 + pixel.G * 150 + pixel.B * 29) >> 8;
                    var binary = (byte)(gray > threshold ? 255 : 0);
                    pixel = new Rgba32(binary, binary, binary, pixel.A);
                }
            }
        });
    }

    private static Image<Rgba32> ToMeikiImage(SimpleBitmap bitmap)
    {
        var image = new Image<Rgba32>(bitmap.GetWidth(), bitmap.GetHeight());
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
                    var si = x * 4;
                    // SimpleBitmap stores BGRA; ImageSharp Rgba32 stores RGBA
                    targetRow[x] = new Rgba32(
                        sourceRow[si + 2], // R
                        sourceRow[si + 1], // G
                        sourceRow[si + 0], // B
                        sourceRow[si + 3]);// A
                }
            }
        });
        return image;
    }

    private void OcrMeikiImage(Image<Rgba32> image, TextWriter writer)
    {
        var boxes = MeikiRunDetect(image);
        var lines = MeikiRunRecognize(image, boxes);
        WriteMeikiResult(lines, writer);
    }

    private static void WriteMeikiResult(List<(string Text, float Confidence, float Height)> lines, TextWriter writer)
    {
        if (lines.Count == 0) return;
        if (lines.Count == 1)
        {
            writer.WriteLine(lines[0].Text);
            return;
        }

        var wroteLine = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Filter out ruby (small annotations) based on box height,
            // consistent with OneOcr/OneOcrSharp which use Y3-Y1 < 30
            if (line.Height < 30f) continue;

            if (wroteLine)
                writer.Write("\\N");

            writer.Write(line.Text);
            wroteLine = true;
        }

        if (wroteLine)
            writer.WriteLine();
    }

    // ═══ Image → CHW float ═══════════════════════════════════════════
    private static float[] MeikiToCHW(Image<Rgba32> img, int tw, int th)
    {
        float s = Math.Min((float)tw / img.Width, (float)th / img.Height);
        int nw = (int)(img.Width * s), nh = (int)(img.Height * s);
        using var r = img.Clone(x => x.Resize(nw, nh));
        var pix = new byte[nw * nh * 4];
        r.CopyPixelDataTo(pix);
        var ret = new float[3 * th * tw];
        float inv = 1f / 255f;
        for (int y = 0; y < nh; y++)
        {
            int sr = y * nw * 4, doff = y * tw;
            for (int x = 0; x < nw; x++)
            {
                int si = sr + x * 4, di = doff + x;
                ret[0 * th * tw + di] = pix[si] * inv;
                ret[1 * th * tw + di] = pix[si + 1] * inv;
                ret[2 * th * tw + di] = pix[si + 2] * inv;
            }
        }
        return ret;
    }

    // ═══ Detection ═══════════════════════════════════════════════════
    private List<(float X1, float Y1, float X2, float Y2, float Score)> MeikiRunDetect(Image<Rgba32> img)
    {
        float sc = Math.Min((float)MeikiDetW / img.Width, (float)MeikiDetH / img.Height);
        var p = MeikiToCHW(img, MeikiDetW, MeikiDetH);
        var t = new DenseTensor<float>(p, new[] { 1, 3, MeikiDetH, MeikiDetW });
        var os = new DenseTensor<long>(new[] { 1, 2 }) { [0, 0] = (long)(MeikiDetW / sc), [0, 1] = (long)(MeikiDetH / sc) };

        using var r = meikiDetSession!.Run(new[] {
            NamedOnnxValue.CreateFromTensor("images", t),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", os),
        });

        var bx = r[1].AsTensor<float>();
        var scs = r[2].AsTensor<float>();
        var out_ = new List<(float, float, float, float, float)>();

        for (int i = 0; i < MeikiMaxN; i++)
        {
            if (scs[0, i] < MeikiDetTh) continue;
            out_.Add((
                MeikiClamp(bx[0, i, 0], 0, img.Width),
                MeikiClamp(bx[0, i, 1], 0, img.Height),
                MeikiClamp(bx[0, i, 2], 0, img.Width),
                MeikiClamp(bx[0, i, 3], 0, img.Height),
                scs[0, i]));
        }

        // Sort by Y
        out_.Sort((a, b) => a.Item2.CompareTo(b.Item2));

        // NMS
        for (int i = 0; i < out_.Count; i++)
        {
            var (ax1, ay1, ax2, ay2, _) = out_[i];
            float aA = (ax2 - ax1) * (ay2 - ay1);
            for (int j = i + 1; j < out_.Count; j++)
            {
                var (bx1, by1, bx2, by2, _) = out_[j];
                float ix1 = Math.Max(ax1, bx1), iy1 = Math.Max(ay1, by1);
                float ix2 = Math.Min(ax2, bx2), iy2 = Math.Min(ay2, by2);
                if (ix1 >= ix2 || iy1 >= iy2) continue;
                float inter = (ix2 - ix1) * (iy2 - iy1);
                float aB = (bx2 - bx1) * (by2 - by1);
                if (inter / Math.Min(aA, aB) > 0.5f)
                {
                    if (aA >= aB) out_.RemoveAt(j--); else { out_.RemoveAt(i--); break; }
                }
            }
        }

        return out_;
    }

    // ═══ Recognition ════════════════════════════════════════════════
    private List<(string Text, float Confidence, float Height)> MeikiRunRecognize(Image<Rgba32> img,
        List<(float X1, float Y1, float X2, float Y2, float Score)> boxes)
    {
        int N = boxes.Count;
        if (N == 0) return new();

        var bp = new float[N * 3 * MeikiRecH * MeikiRecW];
        float inv = 1f / 255f;

        for (int b = 0; b < N; b++)
        {
            var (x1, y1, x2, y2, _) = boxes[b];
            int cw = Math.Max(1, (int)(x2 - x1)), ch = Math.Max(1, (int)(y2 - y1));

            // Merge Crop + Resize into one Clone call
            float th = MeikiRecH;
            int nw = (int)Math.Round(cw * th / ch);
            if (nw > MeikiRecW) { nw = MeikiRecW; th = MeikiRecH * MeikiRecW / (float)cw; }
            int thi = Math.Max(1, (int)th);

            using var rs = img.Clone(x =>
            {
                x.Crop(new Rectangle((int)x1, (int)y1, cw, ch));
                x.Resize(nw, thi);
            });

            var pd = new byte[nw * thi * 4];
            rs.CopyPixelDataTo(pd);

            int bo = b * 3 * MeikiRecH * MeikiRecW;
            for (int y = 0; y < thi; y++)
            {
                int sr = y * nw * 4;
                for (int x = 0; x < nw; x++)
                {
                    int si = sr + x * 4, idx = bo + y * MeikiRecW + x;
                    bp[idx] = pd[si] * inv;
                    bp[bo + 1 * MeikiRecH * MeikiRecW + y * MeikiRecW + x] = pd[si + 1] * inv;
                    bp[bo + 2 * MeikiRecH * MeikiRecW + y * MeikiRecW + x] = pd[si + 2] * inv;
                }
            }
        }

        var inp = new DenseTensor<float>(bp, new[] { N, 3, MeikiRecH, MeikiRecW });
        var os_ = new DenseTensor<long>(new[] { N, 2 });
        for (int b = 0; b < N; b++) { os_[b, 0] = MeikiRecW; os_[b, 1] = MeikiRecH; }

        using var r_ = meikiRecSession!.Run(new[] {
            NamedOnnxValue.CreateFromTensor("images", inp),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", os_),
        });

        var cc = r_[0].AsTensor<int>();
        var cb = r_[1].AsTensor<float>();
        var cs = r_[2].AsTensor<float>();
        var out_ = new List<(string Text, float Confidence, float Height)>();

        for (int b = 0; b < N; b++)
        {
            var chs = new List<(char Char, float X, float Conf)>();
            for (int j = 0; j < 48; j++)
            {
                if (cs[b, j] < MeikiRecTh) continue;
                int code = cc[b, j];
                if (code == 0) break;
                chs.Add(((char)code, cb[b, j, 0], cs[b, j]));
            }
            if (chs.Count == 0) continue;

            // Sort characters by X position
            chs.Sort((a, b_) => a.X.CompareTo(b_.X));
            var box = boxes[b];
            out_.Add((new string(chs.Select(c => c.Char).ToArray()), chs.Average(c => c.Conf), box.Y2 - box.Y1));
        }

        return out_;
    }

    // ═══ Utilities ══════════════════════════════════════════════════
    private static float MeikiClamp(float v, float lo, float hi)
        => Math.Max(lo, Math.Min(hi, v));

    private static (string DetPath, string RecPath) ResolveMeikiModelPaths()
    {
        var appDir = AppContext.BaseDirectory;
        var userCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "meikiocr");

        var candidates = new[]
        {
            (Dir: appDir, Label: "app directory"),
            (Dir: userCache, Label: "user cache"),
        };

        string? detPath = null, recPath = null;

        foreach (var (dir, label) in candidates)
        {
            var d = Path.Combine(dir, "meiki.det.onnx");
            var r = Path.Combine(dir, "meiki.rec.onnx");
            if (File.Exists(d) && File.Exists(r))
            {
                detPath = d;
                recPath = r;
                break;
            }
        }

        if (detPath is not null && recPath is not null)
            return (detPath, recPath);

        // Download to user cache
        Directory.CreateDirectory(userCache);
        var detDl = Path.Combine(userCache, "meiki.det.onnx");
        var recDl = Path.Combine(userCache, "meiki.rec.onnx");

        if (!File.Exists(detDl))
            DownloadMeikiModel(
                "https://huggingface.co/rtr46/meiki.text.detect.v0/resolve/main/meiki.text.detect.v0.1.960x544.onnx",
                detDl);

        if (!File.Exists(recDl))
            DownloadMeikiModel(
                "https://huggingface.co/rtr46/meiki.txt.recognition.v0/resolve/main/meiki.text.rec.v0.960x32.onnx",
                recDl);

        return (detDl, recDl);
    }

    private static void DownloadMeikiModel(string url, string destPath)
    {
        Console.WriteLine($"Downloading MeikiOCR model: {Path.GetFileName(destPath)} ...");
        using var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var resp = h.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var s = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var f = File.Create(destPath);
        s.CopyTo(f);
        Console.WriteLine($"   done: {destPath}");
    }
}
