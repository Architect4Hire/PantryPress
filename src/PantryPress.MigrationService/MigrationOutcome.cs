namespace PantryPress.MigrationService;

/// <summary>
/// The process exit code. It starts as failure so that any path that never reaches success (a crash before the
/// worker runs, a stop mid-run) keeps dependents from starting against an unmigrated database.
/// </summary>
internal sealed class MigrationOutcome
{
    public const int Succeeded = 0;
    public const int Failed = 1;

    public int ExitCode { get; private set; } = Failed;

    public void MarkSucceeded() => ExitCode = Succeeded;
}
