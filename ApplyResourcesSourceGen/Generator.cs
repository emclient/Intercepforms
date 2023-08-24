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
                            writer.WriteLine($"System.Diagnostics.Debug.WriteLine(\"ApplyResources{i}({objectType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, \\\"{objectName}\\\")\");");
                            writer.WriteLine($"var control = ({objectType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})value;");

                            var fallbackNeeded = false;
                            if (resxMap.TryGetValue(objectName, out var properties))
                            {
                                foreach (var kvp in properties)
                                {
                                    var property = kvp.Key;
                                    var (type, value) = kvp.Value;
                                    if (string.IsNullOrEmpty(type))
                                    {
                                        writer.WriteLine($"control.{property} = manager.GetString(\"{objectName}.{property}\");");
                                    }
                                    else if (TryConvertToSimpleAssignment(type, value, out var code))
                                    {
                                        writer.WriteLine($"control.{property} = {code};");
                                    }
                                    else
                                    {
                                        fallbackNeeded = true;
                                        writer.WriteLine($"System.Diagnostics.Debug.WriteLine(\"ApplyResources{i}({objectName}.{property} = {value})\");");
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
                case "System.Int32, mscorlib":
                    code = value;
                    return true;
                case "System.Boolean, mscorlib":
                    code = value.ToLower();
                    return true;
                case "System.Drawing.Size, System.Drawing":
                    code = $"new System.Drawing.Size({value})";
                    return true;
                case "System.Drawing.Point, System.Drawing":
                    code = $"new System.Drawing.Point({value})";
                    return true;
                case "System.Drawing.SizeF, System.Drawing":
                    if (!value.Contains("."))
                    {
                        code = $"new System.Drawing.SizeF({value})";
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                    return true;
            }
            if (type.StartsWith("System.Boolean, mscorlib"))
            {
                code = value;
                return true;
            }
            return false;
        }
    }
}

