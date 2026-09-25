using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotProlog.CodeGen.IL;

internal sealed class IlMetadata
{
    private readonly Dictionary<Type, EntityHandle> _types = [];
    private readonly Dictionary<string, AssemblyReferenceHandle> _assemblies = new(StringComparer.Ordinal);
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

    internal MemberReferenceHandle Method(Type owner, string name, bool instance, Type result, params Type[] parameters) =>
        Builder.AddMemberReference(TypeReference(owner), Builder.GetOrAddString(name), Signature(instance, result, parameters));

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
}
