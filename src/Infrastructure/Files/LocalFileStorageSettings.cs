namespace Template.Infrastructure.Files;

/// <summary>
/// Settings for <see cref="LocalDiskFileStorage"/> (ADR-010), bound from <c>Storage:Local</c>. When
/// <see cref="RootPath"/> is empty the impl falls back to a <c>storage/</c> directory under the app
/// base directory, so the dev/test path works with zero configuration.
/// </summary>
public sealed class LocalFileStorageSettings
{
    /// <summary>Filesystem root under which tenant-scoped objects are stored.</summary>
    public string RootPath { get; set; } = "";
}
