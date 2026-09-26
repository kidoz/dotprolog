using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DotProlog.Compiler;
using DotProlog.Runtime;
using DotProlog.Syntax;
using Machine = DotProlog.Runtime.Machine;

namespace DotProlog.CodeGen.IL;

/// <summary>Compiles Prolog directly into an executable ECMA-335 assembly without generating C#.</summary>
public static class IlAssemblyEmitter
{
    /// <summary>The generated type exposing Main and Install(PrologEngine).</summary>
    public const string ProgramTypeName = "DotProlog.Generated.PrologProgram";

    /// <summary>Writes an assembly on success; on source errors returns diagnostics without writing to the stream.</summary>
    /// <param name="sources">Source units in loading order.</param>
    /// <param name="assemblyName">Simple managed assembly name.</param>
    /// <param name="output">Destination stream, left open.</param>
    /// <param name="languageMode">Language profile used for source and the generated application.</param>
    /// <param name="flagOverrides">Initial flag overrides.</param>
    public static IReadOnlyList<Diagnostic> Emit(
        IReadOnlyList<(string Name, string Text)> sources,
        string assemblyName,
        Stream output,
        PrologLanguageMode languageMode = PrologLanguageMode.Modern,
        PrologFlagOverrides? flagOverrides = null
    ) => Emit(sources, assemblyName, output, true, languageMode, flagOverrides);

    // Retain the instruction-per-block path as a semantic and performance reference for tests.
    internal static IReadOnlyList<Diagnostic> Emit(
        IReadOnlyList<(string Name, string Text)> sources,
        string assemblyName,
        Stream output,
        bool fuseBlocks,
        PrologLanguageMode languageMode = PrologLanguageMode.Modern,
        PrologFlagOverrides? flagOverrides = null,
        bool indexFirstArgument = true,
        bool linearVariableFallback = true
    )
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(output);
        var model = CompiledProgramBuilder.Compile(
            sources,
            [],
            languageMode,
            flagOverrides ?? PrologFlagOverrides.None,
            out var diagnostics,
            indexFirstArgument
        );
        if (model is null)
        {
            return diagnostics;
        }
        WriteAssembly(model, assemblyName, output, fuseBlocks, linearVariableFallback);
        return diagnostics;
    }

    internal static void WriteAssembly(
        CompiledProgramModel model,
        string name,
        Stream output,
        bool fuseBlocks = true,
        bool linearVariableFallback = true
    )
    {
        var metadata = new IlMetadata();
        var builder = metadata.Builder;
        var layout = IlBlockLayout.Create(model, fuseBlocks, linearVariableFallback);
        var image = InstallationImage.Encode(model, layout);
        builder.AddModule(
            0,
            builder.GetOrAddString(name + ".dll"),
            builder.GetOrAddGuid(HashIdentity(IdentityChunks(name, image, model.Instructions))),
            default,
            default
        );
        builder.AddAssembly(
            builder.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.Sha256
        );
        builder.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            builder.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1)
        );
        builder.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            builder.GetOrAddString("DotProlog.Generated"),
            builder.GetOrAddString("PrologProgram"),
            metadata.TypeReference(typeof(object)),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1)
        );
        var bodies = new MethodBodyStreamEncoder(new BlobBuilder());
        var blockSignature = metadata.Signature(
            false,
            typeof(bool),
            typeof(Machine.CompiledExecution).MakeByRefType(),
            typeof(CompiledProgram)
        );
        var linearHeads = layout.LinearClauses.Select(clause => clause.Entry).ToHashSet();
        var blockCode = new BlobBuilder();
        var blockFlow = new ControlFlowBuilder();
        List<Type> parameters = new(4);
        for (var i = 0; i < layout.BlockCount; i++)
        {
            // AddMethod copies each completed body into the method stream. Reuse only
            // the scratch buffers, resetting both bytes and branch labels between blocks.
            blockCode.Clear();
            blockFlow.Clear();
            var il = new InstructionEncoder(blockCode, blockFlow);
            var linear = i >= layout.Starts.Count;
            var start = linear ? layout.LinearClauses[i - layout.Starts.Count].Entry : layout.Starts[i];
            var originalBlock = layout.BlockByInstruction[start];
            var end = originalBlock + 1 < layout.Starts.Count ? layout.Starts[originalBlock + 1] : model.Instructions.Count;
            var failed = il.DefineLabel();
            if (linear)
            {
                var clause = layout.LinearClauses[i - layout.Starts.Count];
                il.LoadArgument(0);
                if (clause.Header != OpCode.TrustMe)
                {
                    il.LoadArgument(1);
                    il.LoadConstantI4(clause.Alternative);
                    il.Call(
                        metadata.Method(typeof(CompiledProgram), nameof(CompiledProgram.Target), true, typeof(int), typeof(int))
                    );
                }
                il.LoadArgument(1);
                il.LoadConstantI4(originalBlock);
                il.Call(metadata.Method(typeof(CompiledProgram), nameof(CompiledProgram.Target), true, typeof(int), typeof(int)));
                il.Call(
                    metadata.Method(
                        typeof(Machine.CompiledExecution),
                        clause.Header.ToString(),
                        true,
                        typeof(bool),
                        clause.Header == OpCode.TrustMe ? [typeof(int)] : [typeof(int), typeof(int)]
                    )
                );
                // Share only a safe fused prefix. A wake-capable entry must resume in its
                // original block, otherwise waking would replay the choice-point header.
                end = start;
                if (IlBlockLayout.CanFuse(model.Instructions[start].OpCode))
                {
                    il.OpCode(ILOpCode.Pop);
                    il.LoadArgument(0);
                    il.LoadArgument(1);
                    il.Call(MetadataTokens.MethodDefinitionHandle(originalBlock + 1));
                }
            }
            for (var instruction = start; instruction < end; instruction++)
            {
                EmitOperation(metadata, il, model, layout, model.Instructions[instruction], parameters);
                if (instruction + 1 < end)
                {
                    il.Branch(ILOpCode.Brfalse, failed);
                }
            }
            il.OpCode(ILOpCode.Ret);
            il.MarkLabel(failed);
            if (end - start > 1)
            {
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Ret);
            }
            AddMethod(
                metadata,
                bodies,
                "Block" + i.ToString(CultureInfo.InvariantCulture),
                blockSignature,
                il,
                isPublic: false,
                inline: !linear && linearHeads.Contains(start) && IlBlockLayout.CanFuse(model.Instructions[start].OpCode)
            );
        }
        var createBlocks = EmitBlockArray(metadata, bodies, layout.BlockCount);
        var install = new InstructionEncoder(new BlobBuilder());
        install.LoadArgument(0);
        install.LoadString(builder.GetOrAddUserString(image));
        install.Call(createBlocks);
        install.Call(
            metadata.Method(
                typeof(IlProgramHost),
                nameof(IlProgramHost.Install),
                false,
                typeof(int[]),
                typeof(PrologEngine),
                typeof(string),
                typeof(CompiledPredicateBlock[])
            )
        );
        install.OpCode(ILOpCode.Ret);
        AddMethod(metadata, bodies, "Install", metadata.Signature(false, typeof(int[]), typeof(PrologEngine)), install);
        var main = new InstructionEncoder(new BlobBuilder());
        main.LoadString(builder.GetOrAddUserString(image));
        main.Call(createBlocks);
        main.Call(
            metadata.Method(
                typeof(IlProgramHost),
                nameof(IlProgramHost.Run),
                false,
                typeof(int),
                typeof(string),
                typeof(CompiledPredicateBlock[])
            )
        );
        main.OpCode(ILOpCode.Ret);
        var entry = AddMethod(metadata, bodies, "Main", metadata.Signature(false, typeof(int)), main);
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateExecutableHeader(),
            new MetadataRootBuilder(builder),
            bodies.Builder,
            entryPoint: entry,
            flags: CorFlags.ILOnly,
            deterministicIdProvider: blobs =>
                BlobContentId.FromHash(SHA256.HashData(blobs.SelectMany(blob => blob.GetBytes()).ToArray()))
        );
        var content = new BlobBuilder();
        pe.Serialize(content);
        content.WriteContentTo(output);
    }

    private static IEnumerable<ReadOnlyMemory<char>> IdentityChunks(
        string name,
        string image,
        IReadOnlyList<CompiledInstruction> instructions
    )
    {
        yield return name.AsMemory();
        yield return "\0".AsMemory();
        yield return image.AsMemory();
        var text = new StringBuilder(4096);
        foreach (var instruction in instructions)
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"|{instruction.OpCode}:{instruction.First}:{instruction.Second}:{instruction.FirstReference}:{instruction.NextAddress}"
            );
            if (text.Length >= 4096)
            {
                foreach (var chunk in text.GetChunks())
                {
                    yield return chunk;
                }
                // The hash consumes each chunk before advancing this iterator.
                text.Clear();
            }
        }
        foreach (var chunk in text.GetChunks())
        {
            yield return chunk;
        }
    }

    internal static Guid HashIdentity(IEnumerable<ReadOnlyMemory<char>> chunks)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoder = Encoding.UTF8.GetEncoder();
        Span<byte> bytes = stackalloc byte[4096];
        // Keep encoder state across chunks: a UTF-16 surrogate pair may straddle them.
        foreach (var chunk in chunks)
        {
            var remaining = chunk.Span;
            while (!remaining.IsEmpty)
            {
                encoder.Convert(remaining, bytes, flush: false, out var charsUsed, out var bytesUsed, out _);
                hash.AppendData(bytes[..bytesUsed]);
                remaining = remaining[charsUsed..];
            }
        }
        // Match Encoding.UTF8.GetBytes even when the final character is an unpaired surrogate.
        encoder.Convert([], bytes, flush: true, out _, out var finalBytes, out _);
        hash.AppendData(bytes[..finalBytes]);
        hash.GetHashAndReset(bytes);
        return new Guid(bytes[..16]);
    }

    private static MethodDefinitionHandle AddMethod(
        IlMetadata metadata,
        MethodBodyStreamEncoder bodies,
        string name,
        BlobHandle signature,
        InstructionEncoder il,
        bool isPublic = true,
        bool inline = false
    ) =>
        metadata.Builder.AddMethodDefinition(
            (isPublic ? MethodAttributes.Public : MethodAttributes.Private)
                | MethodAttributes.Static
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | (inline ? MethodImplAttributes.AggressiveInlining : 0),
            metadata.Builder.GetOrAddString(name),
            signature,
            bodies.AddMethodBody(il, maxStack: 8),
            MetadataTokens.ParameterHandle(1)
        );

    private static MethodDefinitionHandle EmitBlockArray(IlMetadata metadata, MethodBodyStreamEncoder bodies, int count)
    {
        var il = new InstructionEncoder(new BlobBuilder());
        il.LoadConstantI4(count);
        il.OpCode(ILOpCode.Newarr);
        il.Token(metadata.TypeReference(typeof(CompiledPredicateBlock)));
        var constructor = metadata.Method(
            typeof(CompiledPredicateBlock),
            ".ctor",
            true,
            typeof(void),
            typeof(object),
            typeof(nint)
        );
        for (var i = 0; i < count; i++)
        {
            il.OpCode(ILOpCode.Dup);
            il.LoadConstantI4(i);
            il.OpCode(ILOpCode.Ldnull);
            il.OpCode(ILOpCode.Ldftn);
            il.Token(MetadataTokens.MethodDefinitionHandle(i + 1));
            il.OpCode(ILOpCode.Newobj);
            il.Token(constructor);
            il.OpCode(ILOpCode.Stelem_ref);
        }
        il.OpCode(ILOpCode.Ret);
        return AddMethod(
            metadata,
            bodies,
            "CreateBlocks",
            metadata.Signature(false, typeof(CompiledPredicateBlock[])),
            il,
            isPublic: false
        );
    }

    private static void EmitOperation(
        IlMetadata metadata,
        InstructionEncoder il,
        CompiledProgramModel model,
        IlBlockLayout layout,
        CompiledInstruction instruction,
        List<Type> parameters
    )
    {
        parameters.Clear();
        il.LoadArgument(0);
        void Integer(int value)
        {
            il.LoadConstantI4(value);
            parameters.Add(typeof(int));
        }
        void Reference(string method, int value, Type result)
        {
            il.LoadArgument(1);
            il.LoadConstantI4(value);
            il.Call(metadata.Method(typeof(CompiledProgram), method, true, result, typeof(int)));
            parameters.Add(result);
        }
        void Target(int address)
        {
            if (model.InstructionByAddress.TryGetValue(address, out var index))
            {
                Reference(nameof(CompiledProgram.Target), layout.BlockByInstruction[index], typeof(int));
            }
            else
            {
                Integer(address);
            }
        }
        var op = instruction.OpCode;
        switch (op)
        {
            case OpCode.Stop:
            case OpCode.Proceed:
            case OpCode.Fail:
                break;
            case OpCode.Call:
            case OpCode.Execute:
            case OpCode.CallBuiltin:
            case OpCode.GetStructureArgument:
            case OpCode.GetStructureSlot:
            case OpCode.PutStructureArgument:
            case OpCode.PutStructureSlot:
                Reference(
                    op == OpCode.CallBuiltin ? nameof(CompiledProgram.Builtin) : nameof(CompiledProgram.Functor),
                    instruction.FirstReference,
                    typeof(int)
                );
                Integer(instruction.Second);
                break;
            case OpCode.EnterDynamic:
                Reference(nameof(CompiledProgram.Functor), instruction.FirstReference, typeof(int));
                break;
            case OpCode.EnterStatic:
                Reference(nameof(CompiledProgram.StaticIndex), instruction.FirstReference, typeof(int));
                if (layout.LinearEntries.Count > 0 && layout.LinearEntries[instruction.FirstReference].First >= 0)
                {
                    var entry = layout.LinearEntries[instruction.FirstReference];
                    // Bound calls use the table and do not need either fallback target.
                    il.LoadArgument(1);
                    parameters.Add(typeof(CompiledProgram));
                    Integer(entry.First);
                    Integer(entry.Alternative);
                }
                break;
            case OpCode.GetConstant:
            case OpCode.PutConstant:
                Reference(nameof(CompiledProgram.Constant), instruction.FirstReference, typeof(Cell));
                Integer(instruction.Second);
                break;
            case OpCode.UnifyConstant:
                Reference(nameof(CompiledProgram.Constant), instruction.FirstReference, typeof(Cell));
                break;
            case OpCode.TryBranch:
            case OpCode.PushCatch:
            case OpCode.PopCatch:
                Integer(instruction.First);
                Target(instruction.Second);
                break;
            case OpCode.Jump:
            case OpCode.TryMeElse:
            case OpCode.RetryMeElse:
                Target(instruction.First);
                break;
            case OpCode.GetVariable:
            case OpCode.GetValue:
            case OpCode.PutVariable:
            case OpCode.PutValue:
                Integer(instruction.First);
                Integer(instruction.Second);
                break;
            case OpCode.Allocate:
            case OpCode.MarkBarrier:
            case OpCode.CutTo:
            case OpCode.SoftCut:
            case OpCode.ReactivateCatch:
            case OpCode.UnifyVariable:
            case OpCode.UnifyValue:
            case OpCode.InitVariable:
                Integer(instruction.First);
                break;
            case OpCode.Deallocate:
            case OpCode.Cut:
            case OpCode.MetaCall:
            case OpCode.TrustMe:
                break;
            default:
                throw new InvalidOperationException($"Opcode {op} cannot be emitted as IL.");
        }
        if (
            op
            is not (
                OpCode.Stop
                or OpCode.Proceed
                or OpCode.Fail
                or OpCode.Execute
                or OpCode.EnterDynamic
                or OpCode.EnterStatic
                or OpCode.Jump
            )
        )
        {
            Target(instruction.NextAddress);
        }
        il.Call(
            metadata.Method(
                typeof(Machine.CompiledExecution),
                op.ToString(),
                true,
                typeof(bool),
                CollectionsMarshal.AsSpan(parameters)
            )
        );
    }
}
