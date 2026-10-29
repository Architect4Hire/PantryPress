using System.Text;
using System.Text.RegularExpressions;
using PantryPress.Domain.Managers.Tenancy;

namespace PantryPress.Domain.Managers.Audit;

/// <summary>What an audit entry may hold. Every refusal is a bug in the writer, never something a person did.</summary>
public static partial class AuditRules
{
    public const int MaxActionLength = 64;

    /// <summary>The prefix of an action on a site, which is always recorded with one (PP2.4).</summary>
    public const string SiteActionPrefix = "site.";

    public const int MaxActorKindLength = 32;

    public const int MaxTargetKindLength = 32;

    public const int MaxTargetIdLength = 128;

    public const int MaxReasonLength = 256;

    /// <summary>A W3C trace id: 16 bytes as lowercase hex.</summary>
    public const int TraceIdLength = 32;

    /// <summary>The start of a whole publishing key (auth.md), which is never stored; a key is named by its stored id.</summary>
    private const string PublishingKeyPrefix = "pp_live_";

    public static bool IsValidAction(string? action) =>
        action is { Length: > 0 and <= MaxActionLength } && ActionPattern().IsMatch(action);

    public static bool IsValidTarget(AuditTarget target) =>
        target.Kind is { Length: > 0 and <= MaxTargetKindLength }
        && KindPattern().IsMatch(target.Kind)
        && target.Id is { Length: > 0 and <= MaxTargetIdLength }
        && TargetIdPattern().IsMatch(target.Id)
        && !target.Id.StartsWith(PublishingKeyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Null, or short trimmed text with no control characters and no email address: a reason says why, never who. The
    /// check runs on the compatibility form, so a full-width or small at sign counts as one.
    /// </summary>
    public static bool IsValidReason(string? reason) =>
        reason is null
        || (reason.Length is > 0 and <= MaxReasonLength
            && reason.Trim().Length == reason.Length
            && !reason.Any(char.IsControl)
            && !NamesAnAddress(reason));

    /// <summary>The stored code for an actor kind; a stable name, not the enum member's.</summary>
    public static string ActorKindCode(AuditActorKind kind) => kind switch
    {
        AuditActorKind.Account => "account",
        AuditActorKind.OperatorConfiguration => "operator-configuration",
        AuditActorKind.System => "system",
        AuditActorKind.DevelopmentSeed => "development-seed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an audit actor kind."),
    };

    internal static void EnsureValid(AuditEvent auditEvent, SiteScope site)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!IsValidAction(auditEvent.Action))
        {
            throw new ArgumentException(
                $"An audit action is lowercase dot-separated segments of at most {MaxActionLength} characters.",
                nameof(auditEvent));
        }

        if (!Enum.IsDefined(auditEvent.Actor.Kind) || (auditEvent.Actor.Kind == AuditActorKind.Account) != auditEvent.Actor.Id.HasValue)
        {
            throw new ArgumentException("An audit actor is one of the AuditActor values.", nameof(auditEvent));
        }

        if (!IsValidTarget(auditEvent.Target))
        {
            throw new ArgumentException(
                $"An audit target is a lowercase kind and an id of at most {MaxTargetIdLength} letters, digits, or . _ : -.",
                nameof(auditEvent));
        }

        if (!IsValidReason(auditEvent.Reason))
        {
            throw new ArgumentException(
                $"An audit reason is trimmed plain text of at most {MaxReasonLength} characters and names no one.",
                nameof(auditEvent));
        }

        if (site == SiteScope.None && auditEvent.Action.StartsWith(SiteActionPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A site action is recorded on the request's site, never without one.", nameof(site));
        }
    }

    private static bool NamesAnAddress(string reason)
    {
        try
        {
            return reason.Normalize(NormalizationForm.FormKC).Contains('@', StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // Not valid Unicode text, so not a reason either.
            return true;
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(\\.[a-z0-9]+(-[a-z0-9]+)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionPattern();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KindPattern();

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetIdPattern();
}
