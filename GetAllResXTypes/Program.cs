using System.Xml;


var types = new HashSet<string>();
foreach (var designerPath in Directory.EnumerateFiles(args[0], "*.designer.cs", SearchOption.AllDirectories))
{
    var resxPath = designerPath.Substring(0, designerPath.Length - ".designer.cs".Length) + ".resx";
    if (!File.Exists(resxPath)) continue;

    using var fs = File.OpenRead(resxPath);
    using var reader = XmlReader.Create(fs);
    while (reader.ReadToFollowing("data"))
    {
        var path = reader.GetAttribute("name");
        if (path is null || path.StartsWith(">>")) continue;
        var dotIndex = path.IndexOf('.');
        if (dotIndex <= 0) continue;
        //var name = path.Substring(0, dotIndex);
        //var property = path.Substring(dotIndex + 1);
        var type = reader.GetAttribute("type");
        if (string.IsNullOrWhiteSpace(type)) continue;
        types.Add(type);
    }

}
foreach (var type in types)
{
    Console.WriteLine(type);
}

// OUTPUT


// System.Boolean, mscorlib
// System.Boolean, System.Private.CoreLib
// System.Int32, mscorlib
// System.Char, mscorlib
// System.Int32, System.Private.CoreLib

// System.Drawing.Point, System.Drawing
// System.Drawing.Point, System.Drawing.Primitives
// System.Drawing.Size, System.Drawing
// System.Drawing.Size, System.Drawing.Primitives
// System.Drawing.SizeF, System.Drawing
// System.Drawing.SizeF, System.Drawing.Primitives

// System.Windows.Forms.Padding, System.Windows.Forms
// System.Windows.Forms.Padding, System.Windows.Forms.Primitives

// System.Windows.Forms.DockStyle, System.Windows.Forms
// System.Windows.Forms.AnchorStyles, System.Windows.Forms
// System.Windows.Forms.TableLayoutSettings, System.Windows.Forms
// System.Windows.Forms.AutoSizeMode, System.Windows.Forms
// System.Windows.Forms.ImeMode, System.Windows.Forms
// System.Windows.Forms.FormStartPosition, System.Windows.Forms
// System.Windows.Forms.FlowDirection, System.Windows.Forms
// System.Drawing.ContentAlignment, System.Drawing
// System.Drawing.Icon, System.Drawing
// System.Windows.Forms.LinkArea, System.Windows.Forms
// System.Windows.Forms.PictureBoxSizeMode, System.Windows.Forms
// System.Windows.Forms.ScrollBars, System.Windows.Forms
// System.Resources.ResXFileRef, System.Windows.Forms
// System.Windows.Forms.Keys, System.Windows.Forms
// System.Windows.Forms.ImageLayout, System.Windows.Forms
// System.Windows.Forms.Orientation, System.Windows.Forms
// System.Drawing.ContentAlignment, System.Drawing.Common
// System.Drawing.Icon, System.Drawing.Common
// System.Resources.ResXNullRef, System.Windows.Forms
// System.Drawing.Bitmap, System.Drawing
// System.Drawing.Color, System.Drawing
// System.Drawing.Font, System.Drawing
// System.Windows.Forms.RightToLeft, System.Windows.Forms