using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ApplyResourcesSourceGen
{
    internal static class TableLayoutSettingsTypeConverter
    {

        public static void WriteCode(IndentWriter writer, string variable, string value)
        {
            XmlDocument tableLayoutSettingsXml = new XmlDocument();
            tableLayoutSettingsXml.LoadXml(value);
            using (writer.WriteBlock())
            {
                writer.WriteLine("var _settings = (System.Windows.Forms.TableLayoutSettings)System.Activator.CreateInstance(typeof(System.Windows.Forms.TableLayoutSettings), nonPublic: true);");
                ParseControls(writer, variable, tableLayoutSettingsXml.GetElementsByTagName("Control"));
                ParseStyles(writer, variable, tableLayoutSettingsXml.GetElementsByTagName("Columns"), columns: true);
                ParseStyles(writer, variable, tableLayoutSettingsXml.GetElementsByTagName("Rows"), columns: false);
                writer.WriteLine($"{variable} = _settings;");
            }
        }

        private static string? GetAttributeValue(XmlNode node, string attribute)
        {
            XmlAttribute? attr = node.Attributes?[attribute];

            return attr?.Value;
        }

        private static int GetAttributeValue(XmlNode node, string attribute, int valueIfNotFound)
        {
            string? attributeValue = GetAttributeValue(node, attribute);

            return int.TryParse(attributeValue, out int result) ? result : valueIfNotFound;
        }

        private static void ParseControls(IndentWriter writer, string variable, XmlNodeList controlXmlFragments)
        {
            foreach (XmlNode controlXmlNode in controlXmlFragments)
            {
                string? name = GetAttributeValue(controlXmlNode, "Name");

                if (!string.IsNullOrEmpty(name))
                {
                    int row = GetAttributeValue(controlXmlNode, "Row",       /*default*/-1);
                    int rowSpan = GetAttributeValue(controlXmlNode, "RowSpan",   /*default*/1);
                    int column = GetAttributeValue(controlXmlNode, "Column",    /*default*/-1);
                    int columnSpan = GetAttributeValue(controlXmlNode, "ColumnSpan", /*default*/1);

                    writer.WriteLine($"_settings.SetRow(\"{name}\", {row});");
                    writer.WriteLine($"_settings.SetColumn(\"{name}\", {column});");
                    writer.WriteLine($"_settings.SetRowSpan(\"{name}\", {rowSpan});");
                    writer.WriteLine($"_settings.SetColumnSpan(\"{name}\", {columnSpan});");
                }
            }
        }

        private static void ParseStyles(IndentWriter writer, string variable, XmlNodeList controlXmlFragments, bool columns)
        {
            //if (columns)
            //{
            //    writer.WriteLine($"_settings.ColumnStyles.Clear();");
            //}
            //else
            //{
            //    writer.WriteLine($"_settings.RowStyles.Clear();");
            //}
            foreach (XmlNode styleXmlNode in controlXmlFragments)
            {
                string? styleString = GetAttributeValue(styleXmlNode, "Styles");

                // styleString will consist of N Column/Row styles serialized in the following format
                // (Percent | Absolute | AutoSize), (24 | 24.4 | 24,4)
                // Two examples:
                // Percent,23.3,Percent,46.7,Percent,30
                // Percent,23,3,Percent,46,7,Percent,30
                // Note we get either . or , based on the culture the TableLayoutSettings were serialized in

                if (!string.IsNullOrEmpty(styleString))
                {
                    int currentIndex = 0;
                    int nextIndex;
                    while (currentIndex < styleString.Length)
                    {
                        // ---- SizeType Parsing -----------------
                        nextIndex = currentIndex;
                        while (char.IsLetter(styleString[nextIndex]))
                        {
                            nextIndex++;
                        }

                        var type = "System.Windows.Forms.SizeType." + styleString.Substring(currentIndex, nextIndex - currentIndex);

                        // ----- Float Parsing --------------
                        // Find the next Digit (start of the float)
                        while (!char.IsDigit(styleString[nextIndex]))
                        {
                            nextIndex++;
                        }

                        // Append digits left of the decimal delimiter(s)
                        StringBuilder floatStringBuilder = new StringBuilder();
                        while ((nextIndex < styleString.Length) && (char.IsDigit(styleString[nextIndex])))
                        {
                            floatStringBuilder.Append(styleString[nextIndex]);
                            nextIndex++;
                        }

                        // Append culture invariant delimiter
                        floatStringBuilder.Append('.');
                        // Append digits right of the decimal delimiter(s)
                        var builderLen = floatStringBuilder.Length;
                        while ((nextIndex < styleString.Length) && (!char.IsLetter(styleString[nextIndex])))
                        {
                            if (char.IsDigit(styleString[nextIndex]))
                            {
                                floatStringBuilder.Append(styleString[nextIndex]);
                            }

                            nextIndex++;
                        }
                        if (builderLen == floatStringBuilder.Length) floatStringBuilder.Append("0");

                        var floatString = floatStringBuilder.ToString();
                        if (!float.TryParse(floatString, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out float width))
                        {
                            floatString = "0";
                        }

                        // Add new Column/Row Style
                        if (columns)
                        {
                            writer.WriteLine($"_settings.ColumnStyles.Add(new ({type}, {floatString}F));");
                        }
                        else
                        {
                            writer.WriteLine($"_settings.RowStyles.Add(new ({type}, {floatString}F));");
                        }

                        // Go to the next Column/Row Style
                        currentIndex = nextIndex;
                    }
                }
            }
        }
    }
}
