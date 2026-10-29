using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using PantryPress.Domain.Managers.Email;
using PantryPress.Domain.Managers.Tenancy;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Sites;
using PantryPress.Domain.Modules.Sites.Business;
using PantryPress.Domain.Modules.Sites.Contracts;
using PantryPress.Domain.Modules.Sites.DataLayer;
using PantryPress.Domain.Modules.Sites.Validation;

namespace PantryPress.Tests.Sites;

// PP2.11: the pure rules behind invitations - when one is open, how long it lasts, what its email says and links to, the
// sealed token in that link, and what an invitation and an acceptance may carry.
public sealed class SiteInvitationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly IValidator<InviteMemberRequest> InviteValidator = new InviteMemberRequestValidator();

    private static readonly IValidator<AcceptInvitationRequest> AcceptValidator = new AcceptInvitationRequestValidator();

    [Fact]
    public void An_invitation_is_open_while_pending_and_before_its_expiry()
    {
        var expires = Now.AddDays(7);

        Assert.True(SiteInvitationRules.IsOpen(SiteInvitationStatus.Pending, expires, Now));
        Assert.True(SiteInvitationRules.IsOpen(SiteInvitationStatus.Pending, expires, expires.AddTicks(-1)));
        Assert.False(SiteInvitationRules.IsOpen(SiteInvitationStatus.Pending, expires, expires));
        Assert.False(SiteInvitationRules.IsOpen(SiteInvitationStatus.Accepted, expires, Now));
        Assert.False(SiteInvitationRules.IsOpen(SiteInvitationStatus.Revoked, expires, Now));
    }

    // PP2.14: accepting is a rule of its own, not fields a facade sets.
    [Fact]
    public void Only_an_open_invitation_is_accepted_and_accepting_records_who_and_when()
    {
        var account = new AccountId(Guid.NewGuid());
        var open = Invitation(SiteInvitationStatus.Pending, Now.AddDays(7));

        SiteInvitationRules.Accept(open, account, Now);

        Assert.Equal((SiteInvitationStatus.Accepted, account, Now), (open.Status, open.AcceptedBy, open.AcceptedAt));
        Assert.Throws<InvalidOperationException>(() => SiteInvitationRules.Accept(open, account, Now));
        Assert.Throws<InvalidOperationException>(
            () => SiteInvitationRules.Accept(Invitation(SiteInvitationStatus.Pending, Now), account, Now));
        Assert.Throws<InvalidOperationException>(
            () => SiteInvitationRules.Accept(Invitation(SiteInvitationStatus.Revoked, Now.AddDays(7)), account, Now));
    }

    private static SiteInvitation Invitation(SiteInvitationStatus status, DateTimeOffset expiresAt) => new()
    {
        Id = new SiteInvitationId(Guid.NewGuid()),
        Email = "cook@example.com",
        NormalizedEmail = "COOK@EXAMPLE.COM",
        Role = SiteRole.Author,
        Status = status,
        InvitedBy = new AccountId(Guid.NewGuid()),
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt,
    };

    [Fact]
    public void An_invitation_lasts_its_lifetime_and_its_email_says_so_in_whole_days()
    {
        Assert.Equal(Now.AddDays(7), SiteInvitationRules.ExpiryFrom(Now, TimeSpan.FromDays(7)));
        Assert.Throws<ArgumentOutOfRangeException>(() => SiteInvitationRules.ExpiryFrom(Now, TimeSpan.Zero));

        Assert.Equal(7, SiteInvitationRules.LifetimeInDays(Now, Now.AddDays(7)));
        Assert.Equal(2, SiteInvitationRules.LifetimeInDays(Now, Now.AddHours(71)));
        Assert.Equal(1, SiteInvitationRules.LifetimeInDays(Now, Now.AddHours(1)));
    }

    [Theory]
    [InlineData(SiteRole.Owner, "an owner")]
    [InlineData(SiteRole.Editor, "an editor")]
    [InlineData(SiteRole.Author, "an author")]
    public void The_email_names_the_site_and_role_and_carries_the_token_in_the_fragment(SiteRole role, string inWords)
    {
        var id = Guid.NewGuid();
        var email = new SiteInvitationEmails("https://pantrypress.app/")
            .Invitation(id, "pat@example.com", "Pie & <Co>", role, 7, "a+b/c=");

        Assert.Equal((id, "pat@example.com"), (email.Id, email.To));
        Assert.DoesNotContain("Pie", email.Subject, StringComparison.Ordinal);
        Assert.Contains($"\"Pie & <Co>\" on PantryPress, as {inWords}", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://pantrypress.app/dashboard/join#token=a%2Bb%2Fc%3D", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("works for 7 days and only once", email.TextBody, StringComparison.Ordinal);

        // The blogger's name is encoded in the HTML part, never markup.
        Assert.Contains("Pie &amp; &lt;Co&gt;", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<Co>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://pantrypress.app/dashboard/join#token=a%2Bb%2Fc%3D\"", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_day_invitation_says_one_day()
    {
        var email = new SiteInvitationEmails("https://pantrypress.app")
            .Invitation(Guid.NewGuid(), "pat@example.com", "Pie", SiteRole.Author, 1, "t");

        Assert.Contains("works for 1 day and only once", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_names_its_site_and_invitation_and_nothing_else_reads_as_one()
    {
        var tokens = Tokens();
        var site = new SiteId(Guid.NewGuid());
        var invitation = new SiteInvitationId(Guid.NewGuid());

        var token = tokens.Create(site, invitation);

        Assert.True(tokens.TryRead(token, out var readSite, out var readInvitation));
        Assert.Equal((site, invitation), (readSite, readInvitation));
        Assert.DoesNotContain(site.Value.ToString("N"), token, StringComparison.OrdinalIgnoreCase);

        var altered = token[..^4] + (token[^4] == 'A' ? 'B' : 'A') + token[^3..];
        foreach (var bad in new[] { string.Empty, "not a token", "!!!!", altered, token + "x" })
        {
            Assert.False(tokens.TryRead(bad, out _, out _), bad);
        }
    }

    // Sealed for another purpose - an account email link, say - a payload is not an invitation, even with the same ring.
    [Fact]
    public void A_payload_sealed_for_another_purpose_is_not_a_token()
    {
        var provider = DataProtection();
        var other = provider.CreateProtector("PantryPress.LinkToken").Protect(
            System.Text.Encoding.UTF8.GetBytes($"1\n{Guid.NewGuid():N}\n{Guid.NewGuid():N}"));

        Assert.False(new SiteInvitationTokens(provider).TryRead(System.Buffers.Text.Base64Url.EncodeToString(other), out _, out _));
    }

    [Theory]
    [InlineData("pat@example.com", "editor", null)]
    [InlineData("Pat.Smith+pie@Example.co.uk", "owner", null)]
    [InlineData("", "editor", "email")]
    [InlineData(" pat@example.com", "editor", "email")]
    [InlineData("Pat <pat@example.com>", "editor", "email")]
    [InlineData("pat@localhost", "editor", "email")]
    [InlineData("pat@example.com", null, "role")]
    public async Task An_invitation_needs_an_address_and_a_role(string email, string? role, string? field)
    {
        SiteRole? parsed = role is null ? null : Enum.Parse<SiteRole>(role, ignoreCase: true);

        var error = await InviteValidator.CheckAsync(new InviteMemberRequest(email, parsed), CancellationToken.None);

        if (field is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            Assert.Equal([field], error.Fields.Keys);
        }
    }

    [Fact]
    public async Task A_role_outside_the_list_is_refused()
    {
        var error = await InviteValidator.CheckAsync(new InviteMemberRequest("pat@example.com", (SiteRole)7), CancellationToken.None);

        Assert.Equal(["role"], error!.Fields.Keys);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("t", true)]
    public async Task An_acceptance_needs_a_token_of_a_tokens_length(string? token, bool valid)
    {
        var error = await AcceptValidator.CheckAsync(new AcceptInvitationRequest(token!), CancellationToken.None);

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public async Task A_token_far_longer_than_ours_is_refused_before_any_cryptography()
    {
        var error = await AcceptValidator.CheckAsync(
            new AcceptInvitationRequest(new string('a', AcceptInvitationRequestValidator.MaxTokenLength + 1)),
            CancellationToken.None);

        Assert.Equal(["token"], error!.Fields.Keys);
    }

    [Theory]
    [InlineData("Pat@Example.com", "pat@example.com")]
    [InlineData("PAT@EXAMPLE.COM", "pat@example.com")]
    public void Two_spellings_of_one_address_compare_alike(string first, string second) =>
        Assert.Equal(EmailAddresses.Normalize(first), EmailAddresses.Normalize(second));

    [Fact]
    public void Composed_and_decomposed_spellings_compare_alike() =>
        Assert.Equal(EmailAddresses.Normalize("josé@example.com"), EmailAddresses.Normalize("josé@example.com"));

    private static IDataProtectionProvider DataProtection() =>
        new ServiceCollection().AddDataProtection().Services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();

    private static SiteInvitationTokens Tokens() => new(DataProtection());
}
