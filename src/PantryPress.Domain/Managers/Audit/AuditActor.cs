namespace PantryPress.Domain.Managers.Audit;

/// <summary>What kind of actor did an audited action.</summary>
public enum AuditActorKind
{
    /// <summary>A signed-in account, or the holder of an account's emailed link.</summary>
    Account = 1,

    /// <summary>The operator's configuration, applied by the migration job (the <c>Platform:Admins</c> list).</summary>
    OperatorConfiguration = 2,

    /// <summary>PantryPress itself, acting on a rule rather than on anyone's request, such as a lockout.</summary>
    System = 3,

    /// <summary>
    /// The local development seed, applied by the migration job in Development only (PP3.9a). Its own kind, so an
    /// entry a developer's machine wrote is never read as an operator's or a person's doing.
    /// </summary>
    DevelopmentSeed = 4,
}

/// <summary>
/// Who did an audited action: a kind, and the account's id when the actor is an account. Never a name or an address.
/// A publishing key joins the kinds with the publishing API (PP18).
/// </summary>
public readonly record struct AuditActor
{
    private AuditActor(AuditActorKind kind, Guid? id)
    {
        Kind = kind;
        Id = id;
    }

    public AuditActorKind Kind { get; }

    /// <summary>The acting account's id; null for every other kind.</summary>
    public Guid? Id { get; }

    public static AuditActor OperatorConfiguration { get; } = new(AuditActorKind.OperatorConfiguration, null);

    public static AuditActor System { get; } = new(AuditActorKind.System, null);

    /// <summary>The development seed, which exists on a developer's machine alone (PP3.9a).</summary>
    public static AuditActor DevelopmentSeed { get; } = new(AuditActorKind.DevelopmentSeed, null);

    public static AuditActor Account(Guid accountId) =>
        accountId == Guid.Empty
            ? throw new ArgumentException("An acting account needs its id.", nameof(accountId))
            : new(AuditActorKind.Account, accountId);

    /// <summary>The stored code, with the account id after it for an account: <c>account:{id}</c>.</summary>
    public override string ToString() =>
        Id is { } id ? $"{AuditRules.ActorKindCode(Kind)}:{id:D}" : AuditRules.ActorKindCode(Kind);
}

/// <summary>
/// What an audited action was done to: a kind such as <c>account</c>, and that thing's id. An id is letters, digits,
/// and <c>. _ : -</c> only, so an email address cannot be one.
/// </summary>
public readonly record struct AuditTarget(string Kind, string Id);
