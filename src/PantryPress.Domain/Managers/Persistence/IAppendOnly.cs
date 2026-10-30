namespace PantryPress.Domain.Managers.Persistence;

/// <summary>
/// A row that is written once and never changed or removed (PP4.4): a published revision is what a reader saw, and what a
/// later build must find exactly as it was (RCP-007, PP-D04). <see cref="AppendOnlyGuard"/> refuses a save that changes or
/// removes one, and a bulk update or delete of one, and each marked table carries a trigger that refuses an update or
/// delete in the database, so raw SQL is refused too. Adding a row is the one write there is.
/// </summary>
public interface IAppendOnly
{
}
