using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotProlog.CodeGen.IL;

namespace DotProlog.Benchmarks;

internal static class CompilerArtifactMetrics
{
    internal static void Write()
    {
        Console.WriteLine(
            "Repetitions,SourceBytes,Instructions,PeBytes,MetadataBytes,Methods,MethodIlBytes,InstallationImageBytes,MemberReferences,DistinctMemberReferences"
        );
        foreach (var repetitions in new[] { 1, 20, 100 })
        {
            var source = CompilerBenchmarkSource.Create(repetitions);
            var model = CompilerBenchmarkSource.Lower([("pipeline.pl", source)]);
            using var output = new MemoryStream();
            IlAssemblyEmitter.WriteAssembly(model, "PipelineBenchmark", output);
            output.Position = 0;
            using var pe = new PEReader(output);
            var metadata = pe.GetMetadataReader();
            var ilBytes = metadata.MethodDefinitions.Sum(handle =>
                pe.GetMethodBody(metadata.GetMethodDefinition(handle).RelativeVirtualAddress).GetILContent().Length
            );
            var distinctReferences = metadata
                .MemberReferences.Select(handle =>
                {
                    var member = metadata.GetMemberReference(handle);
                    return (
                        member.Parent,
                        Name: metadata.GetString(member.Name),
                        Signature: Convert.ToHexString(metadata.GetBlobBytes(member.Signature))
                    );
                })
                .Distinct()
                .Count();
            Console.WriteLine(
                FormattableString.Invariant(
                    $"{repetitions},{Encoding.UTF8.GetByteCount(source)},{model.Instructions.Count},{output.Length},{pe.PEHeaders.CorHeader!.MetadataDirectory.Size},{metadata.MethodDefinitions.Count},{ilBytes},{Convert.FromBase64String(InstallationImage.Encode(model)).Length},{metadata.MemberReferences.Count},{distinctReferences}"
                )
            );
        }
    }
}
