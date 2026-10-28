namespace PantryPress.Domain.Modules.Accounts.Business;

/// <summary>
/// How saving a unit of work ended. It is the module's own vocabulary rather than the repositories', so a facade answers it
/// without naming anything below its data layer (PP2.14).
/// </summary>
internal enum SaveOutcome
{
    Saved,

    /// <summary>Someone else changed the account after it was read (its concurrency stamp moved). Nothing was saved.</summary>
    Conflict,

    /// <summary>A new account lost a race for its email address. Nothing was saved.</summary>
    EmailTaken,
}
