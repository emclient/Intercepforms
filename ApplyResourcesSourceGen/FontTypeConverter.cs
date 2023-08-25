using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ApplyResourcesSourceGen
{
    internal class FontTypeConverter
    {
        public static string? GetCtorParams(string value)
        {
            value = value.Trim();

            // Expected string format: "name[, size[, units[, style=style1[, style2[...]]]]]"
            // Example using 'vi-VN' culture: "Microsoft Sans Serif, 8,25pt, style=Italic, Bold"
            if (value.Length == 0)
            {
                return null;
            }

            var culture = CultureInfo.InvariantCulture; // TODO

            char separator = culture.TextInfo.ListSeparator[0]; // For vi-VN: ','
            string fontName = value; // start with the assumption that only the font name was provided.
            string? style = null;
            string? sizeStr;
            float fontSize = 8.25f;
            var fontStyle = "System.Drawing.FontStyle.Regular";
            var units = "System.Drawing.GraphicsUnit.Point";

            // Get the index of the first separator (would indicate the end of the name in the string).
            int nameIndex = value.IndexOf(separator);

            if (nameIndex < 0)
            {
                return $"\"{fontName}\", {fontSize.ToString(CultureInfo.InvariantCulture)}F, {fontStyle}, {units}";
            }

            // Some parameters are provided in addition to name.
            fontName = value.Substring(0, nameIndex);

            if (nameIndex < value.Length - 1)
            {
                // Get the style index (if any). The size is a bit problematic because it can be formatted differently
                // depending on the culture, we'll parse it last.
                int styleIndex = culture.CompareInfo.IndexOf(value, "style=", CompareOptions.IgnoreCase);

                if (styleIndex != -1)
                {
                    // style found.
                    style = value.Substring(styleIndex);

                    // Get the mid-substring containing the size information.
                    sizeStr = value.Substring(nameIndex + 1, styleIndex - nameIndex - 1);
                }
                else
                {
                    // no style.
                    sizeStr = value.Substring(nameIndex + 1);
                }

                // Parse size.
                (string? size, string? unit) unitTokens = ParseSizeTokens(sizeStr, separator);

                if (unitTokens.size != null)
                {
                    try
                    {
                        fontSize = (float)TypeDescriptor.GetConverter(typeof(float)).ConvertFromString(null, culture, unitTokens.size)!;
                    }
                    catch
                    {
                        // Exception from converter is too generic.
                        throw new ArgumentException();
                    }
                }

                if (unitTokens.unit != null)
                {
                    // ParseGraphicsUnits throws an ArgumentException if format is invalid.
                    units = ParseGraphicsUnits(unitTokens.unit);
                }

                if (style != null)
                {
                    // Parse FontStyle
                    style = style.Substring(6); // style string always starts with style=
                    string[] styleTokens = style.Split(separator);

                    for (int tokenCount = 0; tokenCount < styleTokens.Length; tokenCount++)
                    {
                        fontStyle = "System.Drawing.FontStyle." + styleTokens[tokenCount];
                    }
                }
            }

            return $"\"{fontName}\", {fontSize.ToString(CultureInfo.InvariantCulture)}F, {fontStyle}, {units}";
        }

        private static (string?, string?) ParseSizeTokens(string text, char separator)
        {
            string? size = null;
            string? units = null;

            text = text.Trim();

            int length = text.Length;
            int splitPoint;

            if (length > 0)
            {
                // text is expected to have a format like " 8,25pt, ". Leading and trailing spaces (trimmed above),
                // last comma, unit and decimal value may not appear.  We need to make it ####.##CC
                for (splitPoint = 0; splitPoint < length; splitPoint++)
                {
                    if (char.IsLetter(text[splitPoint]))
                    {
                        break;
                    }
                }

                char[] trimChars = new char[] { separator, ' ' };

                if (splitPoint > 0)
                {
                    size = text.Substring(0, splitPoint);
                    // Trimming spaces between size and units.
                    size = size.Trim(trimChars);
                }

                if (splitPoint < length)
                {
                    units = text.Substring(splitPoint);
                    units = units.TrimEnd(trimChars);
                }
            }

            return (size, units);
        }

        private static string ParseGraphicsUnits(string units) => units switch
        {
            "display" => "System.Drawing.GraphicsUnit.Display",
            "doc" => "System.Drawing.GraphicsUnit.Document",
            "pt" => "System.Drawing.GraphicsUnit.Point",
            "in" => "System.Drawing.GraphicsUnit.Inch",
            "mm" => "System.Drawing.GraphicsUnit.Millimeter",
            "px" => "System.Drawing.GraphicsUnit.Pixel",
            "world" => "System.Drawing.GraphicsUnit.World",
            _ => throw new ArgumentException(),
        };
    }
}
