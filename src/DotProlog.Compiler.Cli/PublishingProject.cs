using System.Xml.Linq;

namespace DotProlog.Compiler.Cli;

internal static class PublishingProject
{
    internal static readonly string[] Libraries =
    [
        "DotProlog.CodeGen.IL",
        "DotProlog.Compiler",
        "DotProlog.Syntax",
        "DotProlog.Runtime",
    ];

    internal static void Write(string directory)
    {
        // Explicit SDK imports let us replace CoreCompile after the SDK defines it. This project
        // contains IL already: csc must never compile a placeholder or generated C# entry point.
        var project = new XElement(
            "Project",
            new XElement(
                "PropertyGroup",
                new XElement("ImportDirectoryBuildProps", "false"),
                new XElement("ImportDirectoryBuildTargets", "false")
            ),
            new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
            new XElement(
                "PropertyGroup",
                new XElement("TargetFramework", "net10.0"),
                new XElement("OutputType", "Exe"),
                new XElement("AssemblyName", "PrologProgram"),
                new XElement("EnableDefaultItems", "false"),
                new XElement("GenerateAssemblyInfo", "false"),
                new XElement("GenerateTargetFrameworkAttribute", "false"),
                new XElement("ProduceReferenceAssembly", "false"),
                new XElement("DebugType", "none"),
                new XElement("PublishAot", "true"),
                new XElement("InvariantGlobalization", "true"),
                new XElement("TreatWarningsAsErrors", "true"),
                new XElement("IlcTreatWarningsAsErrors", "true")
            ),
            new XElement(
                "ItemGroup",
                Libraries.Select(library => new XElement(
                    "Reference",
                    new XAttribute("Include", library),
                    new XElement("HintPath", library + ".dll")
                ))
            ),
            new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
            new XElement(
                "Target",
                new XAttribute("Name", "CoreCompile"),
                new XElement("MakeDir", new XAttribute("Directories", "$(IntermediateOutputPath)")),
                new XElement(
                    "Copy",
                    new XAttribute("SourceFiles", "$(MSBuildProjectDirectory)/PrologProgram.dll"),
                    new XAttribute("DestinationFiles", "@(IntermediateAssembly)")
                )
            )
        );
        new XDocument(project).Save(Path.Combine(directory, "PrologProgram.csproj"));
        File.WriteAllText(
            Path.Combine(directory, "PrologProgram.runtimeconfig.json"),
            """
            {"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}
            """
        );
        foreach (var library in Libraries)
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, library + ".dll"), Path.Combine(directory, library + ".dll"));
        }
    }
}
