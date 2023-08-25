﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ApplyResourcesSourceGen
{
    [Generator]
    public class Generator : IIncrementalGenerator
    {
        private const string InterceptsLocationAttributeSource = 
            """
            // <auto-generated/>

            namespace System.Runtime.CompilerServices;
            
            [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
            public sealed class InterceptsLocationAttribute : Attribute
            {
                public InterceptsLocationAttribute(string filePath, int line, int column)
                {
                }
            }
            """;


        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("InterceptsLocationAttribute.g.cs", SourceText.From(InterceptsLocationAttributeSource, Encoding.UTF8)));

            var resxFiles = context.AdditionalTextsProvider.Where(static af => af.Path.EndsWith(".resx")).Collect();

            var applyResourcesLocations = context.SyntaxProvider
                .CreateSyntaxProvider(predicate: (node, _) => node is InvocationExpressionSyntax, transform: SyntaxProviderLocationTransformer)
                .Where(x => x.Item1 is not null)
                .Collect();

            var locationsAndResxFiles = applyResourcesLocations.Combine(resxFiles).Combine(context.AnalyzerConfigOptionsProvider);
            
            context.RegisterSourceOutput(source: locationsAndResxFiles, action: SourceOutputAction);
        }

        private static (Location, ITypeSymbol, string) SyntaxProviderLocationTransformer(GeneratorSyntaxContext context, CancellationToken ct)
        {
            // Only look into .Designer.cs files
            var location = context.Node.GetLocation();
            if (location.SourceTree is null || !location.SourceTree.FilePath.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                return (null!, null!, null!);

            // Check we are inside InitializeComponent
            var containingMethodInfo = context.Node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (!containingMethodInfo?.Identifier.Text.Equals("InitializeComponent") != false)
                return (null!, null!, null!);
            var containingMethodSymbol = context.SemanticModel.GetDeclaredSymbol(containingMethodInfo, ct);

            // Check this is call to ApplyResources
            var invocationExpression = (InvocationExpressionSyntax)context.Node;
            var invocationSymbol = context.SemanticModel.GetSymbolInfo(invocationExpression, ct);
            var applyResourcesMethod = context.SemanticModel.Compilation
                .GetTypeByMetadataName("System.ComponentModel.ComponentResourceManager")
                ?.GetMembers("ApplyResources")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Parameters.Length == 2);
            if (invocationSymbol.Symbol is not IMethodSymbol methodSymbol ||
                applyResourcesMethod is null ||
                !methodSymbol.Equals(applyResourcesMethod, SymbolEqualityComparer.Default))
                return (null!, null!, null!);

            // Extract objectName argument (assumes a constant string)
            if (invocationExpression.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax literalExpressionSyntax)
                return (null!, null!, null!);
            string objectName = literalExpressionSyntax.Token.ValueText;

            ITypeSymbol objectType = context.SemanticModel.GetTypeInfo(invocationExpression.ArgumentList.Arguments[0].Expression, ct).Type;
            var memberAccessExpression = (MemberAccessExpressionSyntax)invocationExpression.Expression;

            return (memberAccessExpression.Name.GetLocation(), objectType, objectName);
        }

        private void SourceOutputAction(SourceProductionContext context, ((ImmutableArray<(Location, ITypeSymbol, string)> left, ImmutableArray<AdditionalText> right) files, AnalyzerConfigOptionsProvider config) input)
        {
            var builder = new StringBuilder();
            var locationsAndResxFiles = input.files;

            //foreach (var key in input.config.GlobalOptions.Keys)
            //{
            //    input.config.GlobalOptions.TryGetValue(key, out var value);
            //    builder.AppendLine($"// {key} = {value}");
            //}
            //context.AddSource($"config.g.cs", builder.ToString());

            if (!input.config.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDir))
            {
                throw new InvalidOperationException(); // TODO
            }

            var fileNum = 0;
            foreach (var fileGroup in locationsAndResxFiles.left.Where(t => t.Item1.SourceTree is not null).GroupBy(t => t.Item1.SourceTree.FilePath))
            {
                var filePath = fileGroup.Key;
                var filePathWithoutExt = filePath.Substring(0, filePath.Length - ".Designer.cs".Length);
                var resXFileName = filePathWithoutExt + ".resx";
                var resXFile = locationsAndResxFiles.right.FirstOrDefault(f => f.Path == resXFileName);
                if (resXFile == null)
                    continue;

                var resx = resXFile.GetText()?.ToString();
                if (string.IsNullOrWhiteSpace(resx)) continue;

                var resxMap = new Dictionary<string, Dictionary<string, (string type, string value)>>();
                using var reader = XmlReader.Create(new StringReader(resx));
                while (reader.ReadToFollowing("data"))
                {
                    var path = reader.GetAttribute("name");
                    if (path is null || path.StartsWith(">>")) continue;
                    var dotIndex = path.IndexOf('.');
                    if (dotIndex <= 0) continue;
                    var name = path.Substring(0, dotIndex);
                    var property = path.Substring(dotIndex + 1);
                    var type = reader.GetAttribute("type");
                    if (reader.ReadToDescendant("value"))
                    {
                        var value = reader.ReadElementContentAsString();
                        if (!resxMap.TryGetValue(name, out var properties))
                        {
                            resxMap[name] = properties = new();
                        }
                        properties[property] = (type, value);
                    }
                    else
                    {
                        // TODO
                        throw new InvalidOperationException();
                    }
                }

                var relativePathWithoutExt = filePathWithoutExt.Substring(projectDir.Length);
                var filenameWithoutExt = Path.GetFileName(relativePathWithoutExt);

                builder.Clear();
                builder.AppendLine(
                $$"""
                // <auto-generated/>
            
                namespace ApplyResourcesSourceGen;

                """);

                var writer = new IndentWriter(builder);
                writer.WriteLine($"file static class {filenameWithoutExt}Interceptors");
                using (writer.WriteBlock())
                {
                    var i = 1;
                    foreach ((var location, var objectType, var objectName) in fileGroup)
                    {
                        Debug.Assert(location.SourceTree.FilePath == filePath);
                        var lineSpan = location.GetLineSpan();

                        //builder.AppendLine($"// name = {name}, property = {property}, type = {type}, value = {value}");
                        writer.WriteLine();
                        writer.WriteLine($"[global::System.Runtime.CompilerServices.InterceptsLocation(@\"{location.SourceTree.FilePath}\", {lineSpan.StartLinePosition.Line + 1}, {lineSpan.StartLinePosition.Character + 1})]");
                        writer.WriteLine($"public static void ApplyResources{i}(this System.ComponentModel.ComponentResourceManager manager, object value, string objectName)");
                        using (writer.WriteBlock())
                        {
                            //writer.WriteLine($"System.Diagnostics.Debug.WriteLine(\"ApplyResources{i}({objectType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, \\\"{objectName}\\\")\");");
                            writer.WriteLine($"var control = ({objectType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})value;");

                            var fallbackNeeded = false;
                            if (resxMap.TryGetValue(objectName, out var properties))
                            {
                                foreach (var kvp in properties)
                                {
                                    var property = kvp.Key;
                                    if (property == "TrayLocation") continue; // this is for designer only, used for non-visual components
                                    if (property == "TrayHeight") continue; // this is for designer only, used for non-visual components
                                    var (type, value) = kvp.Value;
                                    if (property == "Localizable" && type == "System.Boolean, mscorlib") continue; // HACK sometimes it is not in the metadata element, probably some issue with subclassing
                                    if (string.IsNullOrEmpty(type))
                                    {
                                        if (property == "Text")
                                        {
                                            writer.WriteLine($"control.{property} = manager.GetString(\"{objectName}.{property}\");");
                                        }
                                        // We should not need to deal with Items as they should be done in designer already
                                        //else if (property.StartsWith("Items"))
                                        //{
                                        //    writer.WriteLine($"control.Items.Add(manager.GetString(\"{objectName}.{property}\"));");
                                        //}
                                        else
                                        {
                                            // TODO
                                        }
                                    }
                                    else if (TryConvertToSimpleAssignment(type, value, out var code))
                                    {
                                        writer.WriteLine($"control.{property} = {code};");
                                    }
                                    else if (type == "System.Windows.Forms.TableLayoutSettings, System.Windows.Forms")
                                    {
                                        TableLayoutSettingsTypeConverter.WriteCode(writer, $"control.{property}", value);
                                    }
                                    else if (type is "System.Drawing.Bitmap, System.Drawing" or "System.Drawing.Icon, System.Drawing")
                                    {
                                        var bytes = Convert.FromBase64String(value);
                                        using (writer.WriteBlock())
                                        {
                                            writer.Write("var bytes = new byte[] {");
                                            foreach (var b in bytes)
                                            {
                                                writer.Write(b);
                                                writer.Write(",");
                                            }    
                                            writer.WriteLine("};");
                                            writer.WriteLine("using var ms = new System.IO.MemoryStream(bytes);");
                                            writer.WriteLine($"control.{property} = new {(type switch 
                                            {
                                                "System.Drawing.Bitmap, System.Drawing" => "System.Drawing.Bitmap",
                                                "System.Drawing.Icon, System.Drawing" => "System.Drawing.Icon",
                                                _ => throw new InvalidOperationException()
                                            })}(ms);");
                                        }
                                    }
                                    else if (type == "System.Drawing.Font, System.Drawing" && FontTypeConverter.GetCtorParams(value) is string ctorParams)
                                    {
                                        writer.WriteLine($"control.{property} = new System.Drawing.Font({ctorParams});");
                                    }
                                    else
                                    {
                                        fallbackNeeded = true;
                                        writer.WriteLine($"System.Diagnostics.Debug.WriteLine(\"ApplyResources{i}({objectName}.{property} of type {type})\");");
                                    }
                                }
                            }
                            if (fallbackNeeded)
                            {
                                writer.WriteLine("manager.ApplyResources(value, objectName);");
                            }
                        }
                        builder.AppendLine($"");
                        i++;
                    }
                }

                context.AddSource($"{relativePathWithoutExt}.g.cs", builder.ToString());
                fileNum++;
            }
        }

        private bool TryConvertToSimpleAssignment(string type, string value, out string code)
        {
            code = null;
            if (string.IsNullOrWhiteSpace(type)) return false;
            switch(type)
            {
                case "System.Resources.ResXNullRef, System.Windows.Forms":
                    code = "null";
                    return true;

                case "System.Int32, mscorlib":
                case "System.Int32, System.Private.CoreLib":
                    code = value;
                    return true;
                case "System.Boolean, mscorlib":
                case "System.Boolean, System.Private.CoreLib":
                    code = value.ToLower();
                    return true;
                case "System.Char, mscorlib":
                case "System.Char, System.Private.CoreLib":
                    code = $"'{value}'";
                    return true;

                case "System.Drawing.Point, System.Drawing":
                case "System.Drawing.Point, System.Drawing.Primitives":
                    code = $"new System.Drawing.Point({value})";
                    return true;
                case "System.Drawing.Size, System.Drawing":
                case "System.Drawing.Size, System.Drawing.Primitives":
                    code = $"new System.Drawing.Size({value})";
                    return true;
                case "System.Drawing.SizeF, System.Drawing":
                case "System.Drawing.SizeF, System.Drawing.Primitives":
                    if (!value.Contains("."))
                    {
                        code = $"new System.Drawing.SizeF({value})";
                    }
                    else
                    {
                        // I could not find any instance where it was actually a floating point value
                        throw new NotImplementedException();
                    }
                    return true;
                // TODO System.Drawing.Font - will require convertor
                // ignore System.Drawing.Color it should be set by designer, not resx

                case "System.Windows.Forms.Padding, System.Windows.Forms":
                case "System.Windows.Forms.Padding, System.Windows.Forms.Primitives":
                    code = $"new System.Windows.Forms.Padding({value})";
                    return true;
                case "System.Windows.Forms.LinkArea, System.Windows.Forms":
                    code = $"new System.Windows.Forms.LinkArea({value})";
                    return true;

                // simple enums
                case "System.Windows.Forms.DockStyle, System.Windows.Forms":
                    code = $"System.Windows.Forms.DockStyle.{value}";
                    return true;
                case "System.Windows.Forms.AutoSizeMode, System.Windows.Forms":
                    code = $"System.Windows.Forms.AutoSizeMode.{value}";
                    return true;
                case "System.Windows.Forms.ImeMode, System.Windows.Forms":
                    code = $"System.Windows.Forms.ImeMode.{value}";
                    return true;
                case "System.Windows.Forms.FormStartPosition, System.Windows.Forms":
                    code = $"System.Windows.Forms.FormStartPosition.{value}";
                    return true;
                case "System.Windows.Forms.FlowDirection, System.Windows.Forms":
                    code = $"System.Windows.Forms.FlowDirection.{value}";
                    return true;
                case "System.Windows.Forms.PictureBoxSizeMode, System.Windows.Forms":
                    code = $"System.Windows.Forms.PictureBoxSizeMode.{value}";
                    return true;
                case "System.Windows.Forms.ScrollBars, System.Windows.Forms":
                    code = $"System.Windows.Forms.ScrollBars.{value}";
                    return true;
                case "System.Windows.Forms.ImageLayout, System.Windows.Forms":
                    code = $"System.Windows.Forms.ImageLayout.{value}";
                    return true;
                case "System.Windows.Forms.Orientation, System.Windows.Forms":
                    code = $"System.Windows.Forms.Orientation.{value}";
                    return true;
                case "System.Windows.Forms.RightToLeft, System.Windows.Forms":
                    code = $"System.Windows.Forms.RightToLeft.{value}";
                    return true;
                case "System.Drawing.ContentAlignment, System.Drawing":
                case "System.Drawing.ContentAlignment, System.Drawing.Common":
                    code = $"System.Drawing.ContentAlignment.{value}";
                    return true;

                // flags enums
                case "System.Windows.Forms.AnchorStyles, System.Windows.Forms":
                    code = $"System.Windows.Forms.AnchorStyles.{value.Replace(", ", " | System.Windows.Forms.AnchorStyles.")}";
                    return true;
                case "System.Windows.Forms.Keys, System.Windows.Forms":
                    code = $"System.Windows.Forms.Keys.{value.Replace("+", " | System.Windows.Forms.Keys.")}"; // HACK probably needs to replaced with adapted code from System.Windows.Forms.KeysConverter
                    return true;
            }
            // probably <metadata Localizable only, not sure if we need to set it
            //if (type.StartsWith("System.Boolean, mscorlib"))
            //{
            //    code = value;
            //    return true;
            //}
            return false;
        }
    }
}

