using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using ApplyResourcesSourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ApplyResourcesSourceGen;
using System.ComponentModel;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;

namespace TestConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            MSBuildLocator.RegisterDefaults();

            Task.Run(async () =>
            {
                var workspace = MSBuildWorkspace.Create();
                var project = await workspace.OpenProjectAsync((string)AppContext.GetData("WinFormsProjectPath")!);
                var compilation = await project.GetCompilationAsync()!;
                if (compilation is null)
                {
                    return;
                }
                var trees = compilation.SyntaxTrees.ToList();
                Console.WriteLine(string.Join("\r\n", trees.Skip(1).Select(t => $"{t.FilePath}:\r\n{t}")));
            }).Wait();
        }
    }
}