﻿using static Mobsub.Helper.ColorConv;

namespace Mobsub.Helper;

public class SimpleBitmap
{
    private int width;
    private int height;
    private int stride;
    private byte[]? pixelData;
    public SimpleBitmap(int w, int h)
    {
        width = w;
        height = h;
        stride = width * 4;
        pixelData = new byte[stride * height];
    }
    
    public int GetWidth() => width;
    public int GetHeight() => height;
    public int GetStride() => stride;
    public byte[]? GetPixelData() => pixelData;

    public void SetPixel(int x, int y, ARGB8b argb)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            throw new ArgumentOutOfRangeException();
        SetPixel(x, y, 4, argb);
    }

    public void DrawHorizontalLine(int x0, int y, int x1, ARGB8b argb)
    {
        if (y < 0 || y >= height || x0 >= width || x1 < 0)
            throw new ArgumentOutOfRangeException();

        if (x0 > x1) (x0, x1) = (x1, x0);

        x0 = Math.Max(x0, 0);
        x1 = Math.Min(x1, width - 1);

        var length = (x1 - x0 + 1) * 4;
        SetPixel(x0, y, length, argb);
    }

    private void SetPixel(int x, int y, int length, ARGB8b argb)
    {
        var startIndex = y * stride + x * 4;
        var span = pixelData.AsSpan(startIndex, length);
        for (var i = 0; i < length; i += 4)
        {
            span[i] = argb.Blue;
            span[i + 1] = argb.Green;
            span[i + 2] = argb.Red;
            span[i + 3] = argb.Alpha;
        }
    }

    public void Save(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // BITMAPFILEHEADER, 14 byte
        // https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapfileheader
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + pixelData!.Length);
        writer.Write(0); // Reserved, 2 short
        writer.Write(54); // Offset to pixel data, start from BITMAPFILEHEADER begin

        // BITMAPINFOHEADER, 40 byte (Uncompressed)
        // https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1); // biPlanes
        writer.Write((short)32); // biBitCount
        writer.Write(0); // biCompression (BI_RGB)
        writer.Write(0); // biSizeImage
        writer.Write(0); // biXPelsPerMeter, GDI ignored
        writer.Write(0); // biYPelsPerMeter, GDI ignored
        writer.Write(0);
        writer.Write(0);
        // BI_BITFIELDS
        //writer.Write(0x00FF0000); // Red mask
        //writer.Write(0x0000FF00); // Green mask
        //writer.Write(0x000000FF); // Blue mask
        //writer.Write(0xFF000000); // Alpha mask

        for (int y = height - 1; y >= 0; y--)
        {
            writer.Write(pixelData, y * stride, stride);
        }
    }

    public unsafe void Binarize(byte threshold = 128)
    {
        fixed (byte* p = pixelData)
        {
            var pixel = p;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var r = pixel[0];
                    var g = pixel[1];
                    var b = pixel[2];

                    // byte gray = (byte)(0.2989 * r + 0.587 * g + 0.114 * b);
                    var gray = (r * 299 + g * 587 + b * 114 + 500) / 1000;

                    var binary = gray > threshold ? (byte)255 : (byte)0;

                    pixel[0] = binary;
                    pixel[1] = binary;
                    pixel[2] = binary;
                    pixel[3] = 255;

                    pixel += 4;
                }
            }
        }
    }
    
    public unsafe SimpleBitmap ResizeNearest(int scale)
    {
        var newImages = new SimpleBitmap(width * scale, height * scale);

        fixed (byte* pSrc = pixelData, pDst = newImages.pixelData)
        {
            for (var origY = 0; origY < height; origY++)
            {
                var srcLine = pSrc + origY * stride;
                var newYStart = origY * scale;
                
                for (var dy = 0; dy < scale; dy++)
                {
                    var dstLine = pDst + (newYStart + dy) * newImages.stride;
                    
                    for (var origX = 0; origX < width; origX++)
                    {
                        var pixel = *(uint*)(srcLine + origX * 4);
                        var newXStart = origX * scale;
                        
                        var dstPixel = (uint*)(dstLine + newXStart * 4);
                        for (var dx = 0; dx < scale; dx++)
                        {
                            dstPixel[dx] = pixel;
                        }
                    }
                }
            }
        }

        return newImages;
    }
}
