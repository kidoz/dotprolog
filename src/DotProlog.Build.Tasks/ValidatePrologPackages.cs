using DotProlog.Packages;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace DotProlog.Build.Tasks;

/// <summary>Checks an existing package lock and cache before an incremental build.</summary>
public sealed class ValidatePrologPackages : Task
{
    /// <summary>The package workspace directory.</summary>
    [Required]
    public string PackageDirectory { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            new PackageWorkspace(PackageDirectory).GetLibraries("dotprolog");
            return true;
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or System.Text.Json.JsonException
            )
        {
            Log.LogError($"Package validation failed: {exception.Message}");
            return false;
        }
    }
}
