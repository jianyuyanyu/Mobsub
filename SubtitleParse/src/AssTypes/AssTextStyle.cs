﻿using Microsoft.Extensions.Logging;
using ZLogger;

namespace Mobsub.SubtitleParse.AssTypes;

public partial class AssTextStyle(AssStyle baseStyle)
{
    public readonly AssStyle BaseStyle = baseStyle;

    public void Reset(AssStyle style)
    {
        FontName = style.Fontname;
        FontSize = style.Fontsize;
        Colors = new AssTextColor()
        {
            Primary = style.PrimaryColour,
            Secondary = style.SecondaryColour,
            Outline = style.OutlineColour,
            Back = style.BackColour,
        };
        FontWeight = style.Bold ? 1 : 0;
        FontItalic = style.Italic;
        TextUnderline = style.Underline;
        TextStrikeOut = style.StrikeOut;
        TextScale = new AssTextScale()
        {
            X = style.ScaleX,
            Y = style.ScaleY,
        };
        TextSpacing = style.Spacing;
        // Angle
        // BorderStyle
        Borders = new AssTextBorder()
        {
            X = style.Outline,
            Y = style.Outline,
        };
        Shadows = new AssTextShadow()
        {
            X = style.Shadow,
            Y = style.Shadow,
        };
        // Alignment ??= style.Alignment;
        // MarginL
        // MarginR
        // MarginV
        FontEncoding = style.Encoding;
    }

    public AssTextStyle DeepCopy()
    {
        var textStyle = (AssTextStyle)MemberwiseClone();
        if (Transform is not null)
        {
            textStyle.Transform = new List<AssTagTransform>();
            // not need deep copy?
            textStyle.Transform.AddRange(Transform);
        }

        return textStyle;
    }
}

public class AssTagTransform(ILogger? logger)
{
    public int StartTime = 0;
    public int EndTime = 0;
    public double Accel = 1.0;
    public AssTextStyleTransform TransTextStyle;

    internal void ParseTime1(ReadOnlySpan<char> span) => StartTime = ParseInt(span, "t1");
    internal void ParseTime2(ReadOnlySpan<char> span) => EndTime = ParseInt(span, "t2");
    internal void ParseAccel(ReadOnlySpan<char> span)
    {
        if (!double.TryParse(span, out var v))
        {
            logger?.ZLogWarning($"Useless transformation accel: {span.ToString()}");
            Accel = 1.0;
        }
        else
        {
            Accel = v;
        }
    }
    
    private int ParseInt(ReadOnlySpan<char> span, string name)
    {
        if (!int.TryParse(span, out var v))
        {
            logger?.ZLogWarning($"Useless transformation {name}: {span.ToString()}");
        }
        return v;
    }
}

public class AssTextColor
{
    public AssRGB8 Primary;
    public AssRGB8 Secondary;
    public AssRGB8 Outline;
    public AssRGB8 Back;
}

public struct AssTextBorder
{
    public double X;
    public double Y;
}
public struct AssTextShadow
{
    public double X;
    public double Y;
}
public struct AssTextScale
{
    public double X;
    public double Y;
}

public struct AssTextPosition(double x, double y)
{
    public double X = x;
    public double Y = y;
}