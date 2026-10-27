using FluentValidation;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Time;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Sites.Validation;

// Facade-edge validation for the site requests (backend.md). Messages are plain language. The slug's shape is the slug
// policy's (PP2.8), reported under the same field and message here; whether a site may take it is the facade's question,
// answered alike for every reason (SITE-002).

internal sealed class CreateSiteRequestValidator : AbstractValidator<CreateSiteRequest>
{
    public CreateSiteRequestValidator(ITimeZoneService timeZones)
    {
        RuleFor(r => r.Slug)
            .Must(slug => SlugPolicy.IsWellFormed(slug, out _))
            .WithMessage(SlugPolicy.InvalidMessage);

        RuleFor(r => r.Name).Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Give your site a name.")
            .Must(name => name.Trim().Length <= SiteLimits.MaxNameLength)
            .WithMessage($"Use {SiteLimits.MaxNameLength} characters or fewer.")
            .Must(SiteText.IsSingleLine).WithMessage("Use letters, numbers, and punctuation on one line.");

        RuleFor(r => r.Tagline).Cascade(CascadeMode.Stop)
            .Must(tagline => tagline!.Trim().Length <= SiteLimits.MaxTaglineLength)
            .WithMessage($"Use {SiteLimits.MaxTaglineLength} characters or fewer.")
            .Must(tagline => SiteText.IsSingleLine(tagline!)).WithMessage("Use letters, numbers, and punctuation on one line.")
            .When(r => r.Tagline is not null);

        RuleFor(r => r.TimeZone)
            .Must(zone => zone is { Length: > 0 and <= SiteLimits.MaxTimeZoneLength } && timeZones.IsKnown(zone))
            .WithMessage("Choose your time zone from the list.");

        RuleFor(r => r.Locale)
            .Must(SiteText.IsLocale)
            .WithMessage("Choose your language from the list.")
            .When(r => r.Locale is not null);
    }
}

internal sealed class SlugAvailabilityQueryValidator : AbstractValidator<SlugAvailabilityQuery>
{
    public SlugAvailabilityQueryValidator() =>
        RuleFor(q => q.Slug).Must(slug => SlugPolicy.IsWellFormed(slug, out _)).WithMessage(SlugPolicy.InvalidMessage);
}

internal sealed class ListSitesQueryValidator : AbstractValidator<ListSitesQuery>
{
    public ListSitesQueryValidator()
    {
        RuleFor(q => q.Limit)
            .InclusiveBetween(1, SiteListCursor.MaxLimit)
            .WithMessage($"Ask for 1 to {SiteListCursor.MaxLimit} sites at a time.")
            .When(q => q.Limit is not null);

        RuleFor(q => q.Cursor)
            .Must(cursor => SiteListCursor.TryRead(cursor, out _))
            .WithMessage("We couldn't continue from that page. Start from the first page.")
            .When(q => q.Cursor is not null);
    }
}

internal sealed class InviteMemberRequestValidator : AbstractValidator<InviteMemberRequest>
{
    public InviteMemberRequestValidator()
    {
        RuleFor(r => r.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Enter the email address to invite.")
            .Must(EmailAddresses.IsValid).WithMessage("Enter an email address like name@example.com.");

        RuleFor(r => r.Role)
            .Must(role => role is { } value && Enum.IsDefined(value))
            .WithMessage("Choose the role they'll have: owner, editor, or author.");
    }
}

internal sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    // A sealed invitation token is a couple of hundred characters; anything far longer is not one of ours, and is refused
    // before any cryptography runs.
    public const int MaxTokenLength = 1024;

    public AcceptInvitationRequestValidator() =>
        RuleFor(r => r.Token)
            .Must(token => token is { Length: > 0 and <= MaxTokenLength })
            .WithMessage("Open the link from your invitation email again.");
}
