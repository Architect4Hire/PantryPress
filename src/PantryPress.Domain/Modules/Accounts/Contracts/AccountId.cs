namespace PantryPress.Domain.Modules.Accounts.Contracts;

/// <summary>
/// The id of a PantryPress account (backend.md: strongly typed ids). Other modules refer to accounts only by this, never
/// by <see cref="ApplicationUser"/>.
/// </summary>
public readonly record struct AccountId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
