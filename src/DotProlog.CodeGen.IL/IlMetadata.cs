using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotProlog.CodeGen.IL;

internal sealed class IlMetadata
{
    private readonly Dictionary<Type, EntityHandle> _types = [];
    private readonly Dictionary<string, AssemblyReferenceHandle> _assemblies = new(StringComparer.Ordinal);
    private readonly Dictionary<(EntityHandle Owner, StringHandle Name, BlobHandle Signature), MemberReferenceHandle> _methods =
    [];
    private readonly Dictionary<(bool Instance, Type Result, Type[] Parameters), BlobHandle> _signatures = new(
        new SignatureComparer()
    );
    internal MetadataBuilder Builder { get; } = new();

    internal EntityHandle TypeReference(Type type)
    {
        if (_types.TryGetValue(type, out var handle))
        {
            return handle;
        }
        EntityHandle scope;
        if (type.DeclaringType is { } parent)
        {
            scope = TypeReference(parent);
        }
        else
        {
            var assembly = type.Assembly.GetName();
            var name = assembly.Name!;
            if (!_assemblies.TryGetValue(name, out var reference))
            {
                reference = Builder.AddAssemblyReference(
                    Builder.GetOrAddString(name),
                    assembly.Version!,
                    default,
                    Builder.GetOrAddBlob(assembly.GetPublicKeyToken() ?? []),
                    default,
                    default
                );
                _assemblies.Add(name, reference);
            }
            scope = reference;
        }
        handle = Builder.AddTypeReference(
            scope,
            Builder.GetOrAddString(type.IsNested ? string.Empty : type.Namespace ?? string.Empty),
            Builder.GetOrAddString(type.Name)
        );
        _types.Add(type, handle);
        return handle;
    }

    internal BlobHandle Signature(bool instance, Type result, params Type[] parameters)
    {
        if (_signatures.TryGetValue((instance, result, parameters), out var signature))
        {
            return signature;
        }
        signature = EncodeSignature(instance, result, parameters);
        // Callers may reuse or mutate their arrays. Only cache-owned copies survive the call.
        _signatures.Add((instance, result, parameters.ToArray()), signature);
        return signature;
    }

    private BlobHandle EncodeSignature(bool instance, Type result, Type[] parameters)
    {
        var blob = new BlobBuilder();
        new BlobEncoder(blob)
            .MethodSignature(isInstanceMethod: instance)
            .Parameters(
                parameters.Length,
                returns =>
                {
                    if (result == typeof(void))
                    {
                        returns.Void();
                    }
                    else
                    {
                        Encode(returns.Type(), result);
                    }
                },
                arguments =>
                {
                    foreach (var parameter in parameters)
                    {
                        Encode(
                            arguments.AddParameter().Type(parameter.IsByRef),
                            parameter.IsByRef ? parameter.GetElementType()! : parameter
                        );
                    }
                }
            );
        return Builder.GetOrAddBlob(blob);
    }

    internal MemberReferenceHandle Method(Type owner, string name, bool instance, Type result, params Type[] parameters)
    {
        // Interned signature blobs include the calling convention, return type, and every
        // parameter shape. Handles belong to this builder, so references never cross assemblies.
        var key = (
            Owner: TypeReference(owner),
            Name: Builder.GetOrAddString(name),
            Signature: Signature(instance, result, parameters)
        );
        if (_methods.TryGetValue(key, out var handle))
        {
            return handle;
        }
        handle = Builder.AddMemberReference(key.Owner, key.Name, key.Signature);
        _methods.Add(key, handle);
        return handle;
    }

    private void Encode(SignatureTypeEncoder encoder, Type type)
    {
        if (type.IsSZArray)
        {
            Encode(encoder.SZArray(), type.GetElementType()!);
        }
        else if (type == typeof(int))
        {
            encoder.Int32();
        }
        else if (type == typeof(bool))
        {
            encoder.Boolean();
        }
        else if (type == typeof(string))
        {
            encoder.String();
        }
        else if (type == typeof(object))
        {
            encoder.Object();
        }
        else if (type == typeof(nint))
        {
            encoder.IntPtr();
        }
        else
        {
            encoder.Type(TypeReference(type), type.IsValueType);
        }
    }

    private sealed class SignatureComparer : IEqualityComparer<(bool Instance, Type Result, Type[] Parameters)>
    {
        public bool Equals(
            (bool Instance, Type Result, Type[] Parameters) x,
            (bool Instance, Type Result, Type[] Parameters) y
        ) => x.Instance == y.Instance && x.Result == y.Result && x.Parameters.AsSpan().SequenceEqual(y.Parameters);

        public int GetHashCode((bool Instance, Type Result, Type[] Parameters) signature)
        {
            var hash = new HashCode();
            hash.Add(signature.Instance);
            hash.Add(signature.Result);
            foreach (var parameter in signature.Parameters)
            {
                hash.Add(parameter);
            }
            return hash.ToHashCode();
        }
    }
}
