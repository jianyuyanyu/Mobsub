using System.Text;
using Mobsub.AssTypes;
using Mobsub.Utils;
using System.Diagnostics;

namespace Mobsub.SubtitleParse;

public class AssParse
{
    public static AssData ReadAssFile(FileStream fs)
    {
        var assData = new AssData { };

        var sr = new StreamReader(fs);

        var buffer = new byte[4];
        fs.Read(buffer, 0, 4);
        assData.CharEncoding = DetectEncoding.GuessEncoding(buffer);
        fs.Seek(0, SeekOrigin.Begin);

        int lineNumber = 0;
        int nextChar;
        var line = new StringBuilder();
        var parseFunc = AssSection.ScriptInfo;
        assData.Sections.Add(parseFunc);
        
        var eventFormats = new List<string>();
        int eventFormatLength = 0;

        while (sr.Peek() != '\n')
        {
            var currentChar = sr.Read();
            if (currentChar == '\r')
            {
                assData.CarriageReturn = true;
            }
            else
            {
                line.Append((char)currentChar);
            }
        }
        sr.Read();
        lineNumber += 1;

        if (!line.Equals("[Script Info]".AsSpan()))
        {
            throw new Exception($"{line} is invaild, first line must be section [Script Info].");
        }
        line.Clear();

        var key = string.Empty;
        var value = string.Empty;
        var commaNum = -1;
        while ((nextChar = sr.Peek()) >= 0)
        {
            if (nextChar == '\n')
            {
                sr.Read();
                lineNumber += 1;

                if (line.Length > 0)
                {
                    line.Clear();   // \r\n empty line
                }

                key = sr.ReadLine();
                lineNumber += 1;
                nextChar = sr.Peek();

                parseFunc = key switch
                {
                    "[Script Info]" => throw new Exception($"Line {lineNumber} have duplicate section [Script Info]"),
                    "[V4 Styles]" => AssSection.StylesV4,
                    "[V4+ Styles]" => AssSection.StylesV4P,
                    "[V4++ Styles]" => AssSection.StylesV4PP,
                    "[Events]" => AssSection.Events,
                    "[Fonts]" => AssSection.Fonts,
                    "[Graphics]" => AssSection.Graphics,
                    "[Aegisub Project Garbage]" => AssSection.AegisubProjectGarbage,
                    "[Aegisub Extradata]" => AssSection.AegisubExtradata,
                    _ => throw new Exception($"Unknown section: {key}."),
                };
                
                if (!assData.Sections.Add(parseFunc))
                {
                    throw new Exception($"Duplicate section: {key}");
                }
            }

            switch (parseFunc)
            {
                case AssSection.ScriptInfo:

                    switch (nextChar)
                    {
                        case ';':
                            sr.Read();
                            assData.ScriptInfo.Comment.Add(sr.ReadLine()!.Trim());
                            lineNumber += 1;
                            break;
                        
                        case '!':
                            sr.Read();
                            if (sr.Peek() == ':')
                            {
                                sr.Read();
                            }
                            assData.ScriptInfo.CustomData.Add(sr.ReadLine()!.Trim());
                            lineNumber += 1;
                            break;

                        case ':':
                            key = line.ToString();
                            sr.Read();
                            value = sr.ReadLine()!.Trim();

                            switch (key)
                            {
                                // Functional headers
                                case AssConstants.ScriptInfo.ScriptType:
                                    assData.ScriptInfo.ScriptType = value;
                                    break;

                                case AssConstants.ScriptInfo.PlayResX:
                                case AssConstants.ScriptInfo.PlayResY:
                                case AssConstants.ScriptInfo.LayoutResX:
                                case AssConstants.ScriptInfo.LayoutResY:
                                case AssConstants.ScriptInfo.WrapStyle:
                                    if (int.TryParse(value, out int intValue))
                                    {
                                        switch (key)
                                        {
                                            case AssConstants.ScriptInfo.PlayResX:
                                                assData.ScriptInfo.PlayResX = intValue;
                                                break;
                                            case AssConstants.ScriptInfo.PlayResY:
                                                assData.ScriptInfo.PlayResY = intValue;
                                                break;
                                            case AssConstants.ScriptInfo.LayoutResX:
                                                assData.ScriptInfo.LayoutResX = intValue;
                                                break;
                                            case AssConstants.ScriptInfo.LayoutResY:
                                                assData.ScriptInfo.LayoutResY = intValue;
                                                break;
                                            case AssConstants.ScriptInfo.WrapStyle:
                                                assData.ScriptInfo.WrapStyle = intValue;
                                                break;
                                        }
                                    }
                                    break;

                                case AssConstants.ScriptInfo.Timer:
                                    if (float.TryParse(value, out float floatValue))
                                    {
                                        assData.ScriptInfo.Timer = floatValue;
                                    }
                                    break;

                                case AssConstants.ScriptInfo.ScaledBorderAndShadow:
                                    assData.ScriptInfo.ScaledBorderAndShadow = value == "yes";
                                    break;

                                case AssConstants.ScriptInfo.Kerning:
                                    assData.ScriptInfo.Kerning = value == "yes";
                                    break;

                                case AssConstants.ScriptInfo.YCbCrMatrix:
                                    var l = value.Split('.');
                                    assData.ScriptInfo.YCbCrMatrix.Full = l.Length > 1 && l.First() != "TV";
                                    assData.ScriptInfo.YCbCrMatrix.Matrix = l.Last();
                                    break;

                                // Metadata headers
                                case AssConstants.ScriptInfo.Title:
                                    assData.ScriptInfo.Title = value;
                                    break;
                                case AssConstants.ScriptInfo.OriginalScript:
                                    assData.ScriptInfo.OriginalScript = value;
                                    break;
                                case AssConstants.ScriptInfo.OriginalTranslation:
                                    assData.ScriptInfo.OriginalTranslation = value;
                                    break;
                                case AssConstants.ScriptInfo.OriginalEditing:
                                    assData.ScriptInfo.OriginalEditing = value;
                                    break;
                                case AssConstants.ScriptInfo.OriginalTiming:
                                    assData.ScriptInfo.OriginalTiming = value;
                                    break;
                                case AssConstants.ScriptInfo.ScriptUpdatedBy:
                                    assData.ScriptInfo.ScriptUpdatedBy = value;
                                    break;
                                case AssConstants.ScriptInfo.UpdateDetails:
                                    assData.ScriptInfo.UpdateDetails = value;
                                    break;

                                default:
                                    // assData.ScriptInfo.Others[key] = value;
                                    // break;
                                    throw new Exception($"Unknown key in Script Info: {key}");
                            }
                            
                            if (!assData.ScriptInfo.Orders.Add(key))
                            {
                                throw new Exception($"Duplicate key in Script Info: {key}");
                            }

                            key = string.Empty;
                            line.Clear();
                            lineNumber += 1;
                            break;

                        default:
                            sr.Read();
                            line.Append((char)nextChar);
                            break;
                    }

                    break;

                case AssSection.StylesV4:
                case AssSection.StylesV4P:
                case AssSection.StylesV4PP:

                    var style = new AssStyle() { };

                    switch (nextChar)
                    {
                        case ':':

                            sr.Read();

                            if (line.Equals("Format".AsSpan()))
                            {
                                assData.Styles.Formats = sr.ReadLine()!.Split(',').Select(s => s.Trim()).ToArray();
                            }
                            else if (line.Equals("Style".AsSpan()))
                            {
                                var valueArray = sr.ReadLine()!.Split(',').Select(s => s.Trim()).ToArray();

                                for (var i = 0; i < valueArray.Length; i++)
                                {
                                    switch (assData.Styles.Formats[i])
                                    {
                                        case "Name":
                                            style.Name = valueArray[i];
                                            break;
                                        case "Fontname":
                                            style.Fontname = valueArray[i];
                                            break;
                                        case "Fontsize":
                                            style.Fontsize = float.Parse(valueArray[i]);
                                            break;
                                        case "PrimaryColour":
                                            style.PrimaryColour = AssRGB8.Parse(valueArray[i].AsSpan());
                                            break;
                                        case "SecondaryColour":
                                            style.SecondaryColour = AssRGB8.Parse(valueArray[i].AsSpan());
                                            break;
                                        case "OutlineColour":
                                            style.OutlineColour = AssRGB8.Parse(valueArray[i].AsSpan());
                                            break;
                                        case "BackColour":
                                            style.BackColour = AssRGB8.Parse(valueArray[i].AsSpan());
                                            break;
                                        case "Bold":
                                            style.Bold = short.Parse(valueArray[i]) == -1;
                                            break;
                                        case "Italic":
                                            style.Italic = short.Parse(valueArray[i]) == -1;
                                            break;
                                        case "Underline":
                                            style.Underline = short.Parse(valueArray[i]) == -1;
                                            break;
                                        case "StrikeOut":
                                            style.StrikeOut = short.Parse(valueArray[i]) == -1;
                                            break;
                                        case "ScaleX":
                                            style.ScaleX = float.Parse(valueArray[i]);
                                            break;
                                        case "ScaleY":
                                            style.ScaleY = float.Parse(valueArray[i]);
                                            break;
                                        case "Spacing":
                                            style.Spacing = float.Parse(valueArray[i]);
                                            break;
                                        case "Angle":
                                            style.Angle = float.Parse(valueArray[i]);
                                            break;
                                        case "BorderStyle":
                                            style.BorderStyle = short.Parse(valueArray[i]);
                                            break;
                                        case "Outline":
                                            style.Outline = float.Parse(valueArray[i]);
                                            break;
                                        case "Shadow":
                                            style.Shadow = float.Parse(valueArray[i]);
                                            break;
                                        case "Alignment":
                                            style.Alignment = short.Parse(valueArray[i]);
                                            break;
                                        case "MarginL":
                                            style.MarginL = int.Parse(valueArray[i]);
                                            break;
                                        case "MarginR":
                                            style.MarginR = int.Parse(valueArray[i]);
                                            break;
                                        case "MarginV":
                                            style.MarginV = int.Parse(valueArray[i]);
                                            break;
                                        case "MarginT":
                                            style.MarginT = int.Parse(valueArray[i]);
                                            break;
                                        case "MarginB":
                                            style.MarginB = int.Parse(valueArray[i]);
                                            break;
                                        case "AlphaLevel":
                                            style.AlphaLevel = int.Parse(valueArray[i]);
                                            break;
                                        case "Encoding":
                                            style.Encoding = int.Parse(valueArray[i]);
                                            break;
                                        case "RelativeTo":
                                            style.RelativeTo = int.Parse(valueArray[i]);
                                            break;
                                    }
                                }

                                assData.Styles.Collection.Add(style);
                                if (!assData.Styles.Names.Add(style.Name))
                                {
                                    throw new Exception($"Styles: duplicate style {style.Name}");
                                }
                            }
                            else
                            {
                                throw new Exception($"Styles: invaild format {line}");
                            }
                            lineNumber += 1;

                            line.Clear();
                            break;

                        default:
                            sr.Read();
                            line.Append((char)nextChar);
                            break;
                    }

                    break;

                case AssSection.Events:

                    var scriptType = assData.ScriptInfo.ScriptType;

                    switch (nextChar)
                    {
                        case ';':

                            sr.Read();
                            if (line.Length == 0)
                            {
                                lineNumber += 1;
                                assData.Events.Collection.Add(new AssEvent(){ StartSemicolon = true, Untouched = sr.ReadLine(), lineNumber = lineNumber });
                            }
                            else
                            {
                                line.Append((char)nextChar);
                            }
                            break;

                        case ':':

                            sr.Read();
                            if (line.Equals("Format".AsSpan()))
                            {
                                if (scriptType == "v4.00++")
                                {
                                    throw new Exception($"{scriptType} not have format line");
                                }
                                eventFormats = sr.ReadLine()!.Split(',').Select(s => s.Trim()).ToList();
                                eventFormatLength = eventFormats.Count;
                                assData.Events.Formats = eventFormats.ToArray();
                                lineNumber += 1;
                                line.Clear();
                            }
                            else if (line.Equals("Dialogue".AsSpan()))
                            {
                                assData.Events.Collection.Add(new AssEvent() { IsDialogue = true });
                                commaNum = 0;
                                line.Clear();
                            }
                            else if (line.Equals("Comment".AsSpan()))
                            {
                                assData.Events.Collection.Add(new AssEvent() { IsDialogue = false });
                                commaNum = 0;
                                line.Clear();
                            }
                            else
                            {
                                line.Append((char)nextChar);
                            }

                            break;

                        case ',':

                            sr.Read();
                            switch (eventFormats[commaNum])
                            {
                                case "Marked":
                                    break;
                                case "Layer":
                                    assData.Events.Collection.Last().Layer = int.Parse(line.ToString());
                                    break;
                                case "Start":
                                    assData.Events.Collection.Last().Start = ParseTime(line);
                                    break;
                                case "End":
                                    assData.Events.Collection.Last().End = ParseTime(line);
                                    break;
                                case "Style":
                                    assData.Events.Collection.Last().Style = line.ToString();
                                    break;
                                case "Name":
                                    assData.Events.Collection.Last().Name = line.ToString();
                                    break;
                                case "MarginL":
                                    assData.Events.Collection.Last().MarginL = int.Parse(line.ToString());
                                    break;
                                case "MarginR":
                                    assData.Events.Collection.Last().MarginR = int.Parse(line.ToString());
                                    break;
                                case "MarginV":
                                    assData.Events.Collection.Last().MarginV = int.Parse(line.ToString());
                                    break;
                                case "MarginT":
                                    assData.Events.Collection.Last().MarginT = int.Parse(line.ToString());
                                    break;
                                case "MarginB":
                                    assData.Events.Collection.Last().MarginB = int.Parse(line.ToString());
                                    break;
                                case "Effect":
                                    assData.Events.Collection.Last().Effect = line.ToString();
                                    break;
                                case "Text":
                                    throw new Exception("Effects: Test must be last field.");
                            }

                            commaNum += 1;
                            line.Clear();

                            if (commaNum == eventFormatLength - 1)
                            {
                                var text = sr.ReadLine();
                                if (text is null)
                                {
                                    assData.Events.Collection.Last().Text = [];
                                }
                                else
                                {
                                    assData.Events.Collection.Last().Text = ParseEventText(text.Trim().AsSpan());
                                }
                                lineNumber += 1;
                                assData.Events.Collection.Last().lineNumber = lineNumber;
                            }

                            break;

                        default:
                            sr.Read();
                            line.Append((char)nextChar);
                            break;
                    }

                    break;

                case AssSection.AegisubProjectGarbage:
                    switch (nextChar)
                    {
                        case ':':
                            sr.Read();
                            assData.AegisubProjectGarbage.TryAdd(line.ToString(), sr.ReadLine()!.Trim());
                            lineNumber += 1;
                            line.Clear();
                            break;
                        default:
                            sr.Read();
                            line.Append((char)nextChar);
                            break;
                    }
                    break;

                case AssSection.AegisubExtradata:
                    switch (nextChar)
                    {
                        case ':':
                            sr.Read();
                            assData.AegiusbExtradata.Add(sr.ReadLine()!.Trim());
                            lineNumber += 1;
                            break;
                        default:
                            sr.Read();
                            break;
                    }
                    break;

                case AssSection.Fonts:

                    value = sr.ReadLine()!;
                    lineNumber += 1;

                    if (value.StartsWith("fontname:"))
                    {
                        assData.Fonts.Add(new AssEmbeddedFont() { });
                        var embeddedFontName = value.Split(':')[1].Trim().Split('_');

                        assData.Fonts.Last().OriginalName = embeddedFontName.Length > 2 ? string.Join('_', embeddedFontName.SkipLast(1)) : embeddedFontName[0];
                        var embeddedFontAddition = embeddedFontName.Last().AsSpan();

                        if (embeddedFontAddition.StartsWith("B"))
                        {
                            assData.Fonts.Last().Bold = true;
                            if (embeddedFontAddition[1].Equals('I'))
                            {
                                assData.Fonts.Last().Italic = true;
                            }
                        }
                        else
                        {
                            assData.Fonts.Last().CharacterEncoding = int.Parse(embeddedFontName.Last().Split('.')[0]);
                        }
                    }
                    else
                    {
                        assData.Fonts.Last().Data.Add(value);
                        assData.Fonts.Last().DataLength += value.Length;
                    }

                    break;

                case AssSection.Graphics:

                    value = sr.ReadLine()!;
                    lineNumber += 1;

                    if (value.StartsWith("filename:"))
                    {
                        assData.Graphics.Add(new AssEmbeddedGraphic() { });
                        assData.Graphics.Last().Name = value.Split(':')[1].Trim();
                    }
                    else
                    {
                        assData.Graphics.Last().Data.Add(value);
                        assData.Graphics.Last().DataLength += value.Length;
                    }

                    break;

                // default:
                //     sr.ReadLine();
                //     lineNumber += 1;
                //     break;
            }
        }

        lineNumber += 1;

        fs.Close();
        return assData;
    }

    public static AssData ReadAssFile(string filePath)
    {
        var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return ReadAssFile(fs);
    }

    public static AssTime ParseTime(StringBuilder sb)
    {
        // hours:minutes:seconds:centiseconds
        // 0:00:00.00, number of digits of hours is variable
        var ms = 0;
        var sepPosFirst = 1;

        for (int i = 0; i < sb.Length; i++)
        {
            if (sb[i] == ':')
            {
                sepPosFirst = i;
                break;
            }
        }

        int h = 0;
        for (var i = sepPosFirst; i > -1; i--)
        {
            h += (sb[i] - '0') * (int)Math.Pow(10, i - 1);
        }
        ms += h * 1000 * 60 * 60;

        for (int i = sepPosFirst + 1; i < sb.Length; i++)
        {
            var c = sb[i];
            var n = c - '0';
            
            if (i == sepPosFirst + 1)
            {
                ms += n * 1000 * 60 * 10;
            }
            else if (i == sepPosFirst + 2)
            {
                ms += n * 1000 * 60;
            }
            else if (i == sepPosFirst + 4)
            {
                ms += n * 1000 * 10;
            }
            else if (i == sepPosFirst + 5)
            {
                ms += n * 1000;
            }
            else if (i == sepPosFirst + 6)
            {
                if (c != '.')
                {
                    throw new Exception($"Wrong timestamp in ass: {sb}");
                }
            }
            else if (i == sepPosFirst + 7)
            {
                ms += n * 100;
            }
            else if (i == sepPosFirst + 8)
            {
                ms += n * 10;
            }
            else
            {
                if (c != ':')
                {
                    throw new Exception($"Wrong timestamp in ass: {sb}");
                }
            }
        }

        return new AssTime(ms);
    }

    public static List<char[]> ParseEventText(ReadOnlySpan<char> span)
    {
        var blk = false;
        List<char[]> records = [];
        char c;
        int _start = 0;
        int _end = 0;
        var backslash = false;


        for (var i = 0; i < span.Length; i++)
        {
            if (i == span.Length - 1 && span[i] != '}')
            {
                // In gerneral, blocks are arranged in this order: (text block -) ovr block - text block - ovr block( - text block)
                // but sometimes, text block will be mistakenly identified as ovr block because it start with open brace but does not have close brace until line end
                // It should be a type error, but parse will ignore it
                // Not considering libass extensions, such as \{ and \}
                if (blk)
                {
                    records.Add(span[_start..].ToArray());
                }
                else
                {
                    records.Add(span[_end..].ToArray());
                }
            }
            else
            {
                c = span[i];
                switch (c)
                {
                    case AssConstants.StartOvrBlock:
                        if (!blk)
                        {
                            if (i > 0 && _end != i)
                            {
                                records.Add(span[_end..i].ToArray());
                            }
                            _start = i;
                            blk = true;
                        }
                        break;
                    case AssConstants.EndOvrBlock:
                        if (blk)
                        {
                            _end = i + 1;
                            records.Add(span[_start.._end].ToArray());
                            blk = false;
                        }
                        break;
                    case AssConstants.BackSlash:
                        if (!blk)
                        {
                            _start = i;
                            backslash = true;
                            if (_end != _start)
                            {
                                records.Add(span[_end.._start].ToArray());
                            }
                            _end = _start;
                        }
                        break;
                    case AssConstants.NBSP or AssConstants.WordBreaker or AssConstants.LineBreaker:
                        if (backslash)
                        {
                            _end = i + 1;
                            backslash = false;
                            records.Add(span[_start.._end].ToArray());
                        }
                        break;
                    default:
                        backslash = false;
                        break;
                }
            }
        }
        
        Debug.Assert(records.Sum(l => l.Length) == span.Length, $"Parse records length is {records.Sum(l => l.Length)}, should be {span.Length}");

        return records;
    }
    public static void WriteAssFile(AssData data, string filePath, bool forceEnv, bool ctsRounding)
    {
        var newline = forceEnv ? [.. Environment.NewLine] : (data.CarriageReturn ? new char[]{'\r', '\n'} : ['\n']);
        var charEncoding = forceEnv ? DetectEncoding.EncodingRefOS() : data.CharEncoding;

        using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write);
        using var memStream = new MemoryStream();
        using var sw = new StreamWriter(memStream, charEncoding);
        foreach (var s in data.Sections)
        {
            switch (s)
            {
                case AssSection.ScriptInfo:
                    data.ScriptInfo.Write(sw, newline);
                    break;
                case AssSection.StylesV4P:
                case AssSection.StylesV4:
                    data.Styles.Write(sw, newline, data.ScriptInfo.ScriptType);
                    break;
                case AssSection.Events:
                    data.Events.Write(sw, newline, ctsRounding);
                    break;
                case AssSection.Fonts:
                    sw.Write("[Fonts]");
                    sw.Write(newline);
                    foreach (var o in data.Fonts)
                    {
                        o.Write(sw, newline);
                    }
                    break;
                case AssSection.Graphics:
                    sw.Write("[Graphics]");
                    sw.Write(newline);
                    foreach (var o in data.Fonts)
                    {
                        o.Write(sw, newline);
                    }
                    break;
                case AssSection.AegisubProjectGarbage:
                    sw.Write("[Aegisub Project Garbage]");
                    sw.Write(newline);
                    foreach (var kvp in data.AegisubProjectGarbage)
                    {
                        sw.Write($"{kvp.Key}: {kvp.Value}");
                        sw.Write(newline);
                    }
                    sw.Write(newline);
                    break;
                case AssSection.AegisubExtradata:
                    sw.Write("[Aegisub Extradata]");
                    sw.Write(newline);
                    for (var i = 0; i < data.AegiusbExtradata.Count; i++)
                    {
                        sw.Write(data.AegiusbExtradata.ToArray()[i]);
                        sw.Write(newline);
                    }
                    sw.Write(newline);
                    break;
            }
        }
        sw.Flush();

        memStream.Seek(0, SeekOrigin.Begin);
        memStream.CopyTo(fileStream);
        fileStream.Close();
    }
    public static void WriteAssFile(AssData data, string filePath) => WriteAssFile(data, filePath, false, false);

}