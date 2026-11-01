using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotProlog.Compiler;

namespace DotProlog.CodeGen.IL;

internal sealed class IlDebugInfo
{
    private readonly List<List<IlSequencePoint>> _methods = [];

    internal void AddMethod() => _methods.Add([]);

    internal void AddSequencePoint(int offset, CompiledSourceLocation? source)
    {
        if (
            source is not { } location
            || location.StartLine <= 0
            || location.EndLine < location.StartLine
            || (location.EndLine == location.StartLine && location.EndColumn <= location.StartColumn)
            || location.StartLine >= SequencePoint.HiddenLine
            || location.EndLine >= SequencePoint.HiddenLine
            || location.StartColumn > ushort.MaxValue
            || location.EndColumn > ushort.MaxValue
        )
        {
            return;
        }

        _methods[^1].Add(new IlSequencePoint(offset, location));
    }

    internal DebugDirectoryBuilder Write(
        MetadataBuilder typeSystem,
        MethodDefinitionHandle entryPoint,
        Stream output,
        string pdbPath
    )
    {
        ArgumentNullException.ThrowIfNull(typeSystem);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdbPath);
        if (_methods.Count != typeSystem.GetRowCount(TableIndex.MethodDef))
        {
            throw new InvalidOperationException("Debug method count does not match the emitted assembly.");
        }

        var metadata = new MetadataBuilder();
        Dictionary<string, DocumentHandle> documents = new(StringComparer.Ordinal);
        foreach (var method in _methods)
        {
            DocumentHandle document = default;
            var singleDocument = true;
            foreach (var point in method)
            {
                var pointDocument = Document(point.Source.Document);
                if (document.IsNil)
                {
                    document = pointDocument;
                }
                else if (document != pointDocument)
                {
                    singleDocument = false;
                }
            }

            metadata.AddMethodDebugInformation(singleDocument ? document : default, SequencePoints(method, singleDocument));
        }

        var pdb = new PortablePdbBuilder(metadata, typeSystem.GetRowCounts(), entryPoint, IlAssemblyEmitter.HashContent);
        var content = new BlobBuilder();
        var contentId = pdb.Serialize(content);
        content.WriteContentTo(output);
        var debug = new DebugDirectoryBuilder();
        debug.AddCodeViewEntry(pdbPath, contentId, 0x0100);
        debug.AddReproducibleEntry();
        return debug;

        DocumentHandle Document(string name)
        {
            if (!documents.TryGetValue(name, out var handle))
            {
                handle = metadata.AddDocument(metadata.GetOrAddDocumentName(name), default, default, default);
                documents.Add(name, handle);
            }

            return handle;
        }

        BlobHandle SequencePoints(List<IlSequencePoint> points, bool singleDocument)
        {
            if (points.Count == 0)
            {
                return default;
            }

            var writer = new BlobWriter(256);
            writer.WriteCompressedInteger(0);
            CompiledSourceLocation previous = default;
            var hasPrevious = false;
            var previousOffset = 0;
            foreach (var point in points)
            {
                var source = point.Source;
                if (!singleDocument && (!hasPrevious || previous.Document != source.Document))
                {
                    if (hasPrevious)
                    {
                        writer.WriteCompressedInteger(0);
                    }
                    writer.WriteCompressedInteger(MetadataTokens.GetRowNumber(Document(source.Document)));
                }

                writer.WriteCompressedInteger(hasPrevious ? point.Offset - previousOffset : point.Offset);
                var deltaLines = source.EndLine - source.StartLine;
                var deltaColumns = source.EndColumn - source.StartColumn;
                writer.WriteCompressedInteger(deltaLines);
                if (deltaLines == 0)
                {
                    writer.WriteCompressedInteger(deltaColumns);
                }
                else
                {
                    writer.WriteCompressedSignedInteger(deltaColumns);
                }

                if (hasPrevious)
                {
                    writer.WriteCompressedSignedInteger(source.StartLine - previous.StartLine);
                    writer.WriteCompressedSignedInteger(source.StartColumn - previous.StartColumn);
                }
                else
                {
                    writer.WriteCompressedInteger(source.StartLine);
                    writer.WriteCompressedInteger(source.StartColumn);
                }

                previous = source;
                hasPrevious = true;
                previousOffset = point.Offset;
            }

            return metadata.GetOrAddBlob(writer.ToArray());
        }
    }

    private readonly record struct IlSequencePoint(int Offset, CompiledSourceLocation Source);
}
