namespace PantryPress.Domain.Modules.Recipes.Contracts;

/// <summary>What an ingredient line's quantity is (RCP-002; PP4.2). Stored as these numbers, so they never change.</summary>
public enum QuantityKind
{
    /// <summary>One amount: <c>2</c>, <c>1½</c>, <c>0.5</c>.</summary>
    Amount = 0,

    /// <summary>A low and a high amount, low first: <c>2–3</c>.</summary>
    Range = 1,

    /// <summary>No amount at all: the blogger leaves it to the cook. Never scaled (RCP-004).</summary>
    ToTaste = 2,
}
