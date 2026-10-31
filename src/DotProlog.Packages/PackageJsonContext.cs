using System.Text.Json.Serialization;

namespace DotProlog.Packages;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(PackageLock))]
[JsonSerializable(typeof(Dictionary<string, List<PackageSource>>))]
internal sealed partial class PackageJsonContext : JsonSerializerContext;
