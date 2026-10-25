using System.Runtime.CompilerServices;

namespace PantryPress.Tests;

/// <summary>
/// The Domain project's source tree, found from this file's own path, for the tests that scan source rather than
/// metadata (PP1.5): modules share one assembly, and a shared constant is inlined by the compiler.
/// </summary>
internal static class DomainSource
{
    public static DirectoryInfo Project { get; } =
        new(Path.GetFullPath(Path.Combine(TestsDirectory(), "..", "PantryPress.Domain")));

    public static DirectoryInfo Modules { get; } = new(Path.Combine(Project.FullName, "Modules"));

    private static string TestsDirectory([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}
