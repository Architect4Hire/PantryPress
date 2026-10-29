using FluentValidation;
using PantryPress.Domain.Managers.Results;
using PantryPress.Domain.Managers.Validation;
using PantryPress.Domain.Modules.Accounts;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Accounts.Validation;

namespace PantryPress.Tests.Accounts;

// PP1.2 and PP1.3: the pure account rules (ACCT-001, ACCT-003).
public sealed class AccountRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("cook@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void Ordinary_addresses_are_valid(string email)
    {
        Assert.True(EmailAddressRules.IsValid(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cook")]
    [InlineData("cook@localhost")]
    [InlineData("Cook <cook@example.com>")]
    [InlineData(" cook@example.com")]
    [InlineData("cook@example.com\r\nBcc: x@example.com")]
    public void Malformed_or_decorated_addresses_are_invalid(string? email)
    {
        Assert.False(EmailAddressRules.IsValid(email));
    }

    [Fact]
    public void Addresses_longer_than_the_SMTP_limit_are_invalid()
    {
        var email = new string('a', 64) + "@" + string.Join('.', Enumerable.Repeat(new string('b', 60), 4)) + ".com";

        Assert.True(email.Length > EmailAddressRules.MaxLength);
        Assert.False(EmailAddressRules.IsValid(email));
    }

    [Theory]
    [InlineData("someone@mailinator.com")]
    [InlineData("someone@MAILINATOR.com")]
    [InlineData("someone@anything.mailinator.com")]
    public void Disposable_domains_and_their_subdomains_are_blocked(string email)
    {
        Assert.True(DisposableEmailDomains.IsDisposable(email));
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@outlook.com")]
    [InlineData("someone@mailinator.com.example.org")]
    public void Ordinary_providers_are_not_blocked(string email)
    {
        Assert.False(DisposableEmailDomains.IsDisposable(email));
    }

    [Fact]
    public void The_embedded_disposable_list_is_loaded()
    {
        Assert.True(DisposableEmailDomains.Count > 9_000);
    }

    [Fact]
    public async Task Sign_up_refuses_disposable_addresses_and_short_or_long_passwords_in_plain_words()
    {
        var validator = new RegisterAccountRequestValidator();

        var disposable = await validator.CheckAsync(new RegisterAccountRequest("a@mailinator.com", "long enough phrase"), default);
        var tooShort = await validator.CheckAsync(new RegisterAccountRequest("a@example.com", "short"), default);
        var tooLong = await validator.CheckAsync(new RegisterAccountRequest("a@example.com", new string('x', 129)), default);
        var missing = await validator.CheckAsync(new RegisterAccountRequest(null!, null!), default);

        Assert.Contains("Temporary inboxes", Assert.Single(disposable!.Fields["email"]), StringComparison.Ordinal);
        Assert.Contains("at least 12", Assert.Single(tooShort!.Fields["password"]), StringComparison.Ordinal);
        Assert.Contains("128", Assert.Single(tooLong!.Fields["password"]), StringComparison.Ordinal);
        Assert.Equal(["email", "password"], missing!.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.Null(await validator.CheckAsync(new RegisterAccountRequest("a@example.com", "twelve chars"), default));
    }

    [Fact]
    public async Task A_missing_request_is_a_validation_error_not_a_crash()
    {
        var error = await new ForgotPasswordRequestValidator().CheckAsync<ForgotPasswordRequest>(null, default);

        Assert.Equal(ErrorKind.Validation, error!.Kind);
        Assert.Equal("validation-failed", error.Code);
    }

    [Fact]
    public async Task Link_and_code_inputs_are_bounded()
    {
        IValidator<ResetPasswordRequest> reset = new ResetPasswordRequestValidator();
        var huge = await reset.CheckAsync(new ResetPasswordRequest(Guid.NewGuid(), new string('t', 1025), "a fine new password"), default);
        var empty = await reset.CheckAsync(new ResetPasswordRequest(Guid.Empty, "", "a fine new password"), default);

        Assert.Contains("token", huge!.Fields.Keys);
        Assert.Equal(["accountId", "token"], empty!.Fields.Keys.Order(StringComparer.Ordinal));
        Assert.NotNull(await new EnableTwoFactorRequestValidator().CheckAsync(new EnableTwoFactorRequest("ABCDE-FGHJK"), default));
        Assert.Null(await new SecondFactorRequestValidator().CheckAsync(new SecondFactorRequest("ABCDE-FGHJK"), default));
        Assert.Null(await new SecondFactorRequestValidator().CheckAsync(new SecondFactorRequest("123 456"), default));
    }

    [Fact]
    public void Throttle_allows_one_email_a_minute()
    {
        Assert.True(EmailThrottle.TryTake(null, Start, out var state));
        Assert.False(EmailThrottle.TryTake(state, Start.AddSeconds(59), out var unchanged));
        Assert.Equal(state, unchanged);
        Assert.True(EmailThrottle.TryTake(state, Start.AddSeconds(60), out _));
    }

    [Fact]
    public void Throttle_allows_five_emails_an_hour_then_starts_a_new_window()
    {
        string? state = null;
        for (var i = 0; i < EmailThrottle.MaxPerWindow; i++)
        {
            Assert.True(EmailThrottle.TryTake(state, Start.AddMinutes(i * 2), out var next));
            state = next;
        }

        Assert.False(EmailThrottle.TryTake(state, Start.AddMinutes(30), out _));
        Assert.False(EmailThrottle.TryTake(state, Start.AddMinutes(59), out _));
        Assert.True(EmailThrottle.TryTake(state, Start.AddMinutes(60), out _));
    }

    [Fact]
    public void Throttle_treats_an_unreadable_state_as_none()
    {
        Assert.True(EmailThrottle.TryTake("garbage", Start, out _));
    }

    [Theory]
    [InlineData("https://pantrypress.app", true)]
    [InlineData("https://localhost:7044", true)]
    [InlineData("http://localhost:5239", true)]
    [InlineData("http://cook.localhost:5239", true)]
    [InlineData("http://pantrypress.app", false)]
    [InlineData("https://pantrypress.app/admin", false)]
    [InlineData("https://pantrypress.app/?x=1", false)]
    [InlineData("https://user@pantrypress.app", false)]
    [InlineData("pantrypress.app", false)]
    [InlineData("", false)]
    public void Email_links_need_an_absolute_HTTPS_origin_except_on_this_machine(string url, bool valid)
    {
        Assert.Equal(valid, new AccountsOptions { AppBaseUrl = url }.IsValid());
    }

    [Fact]
    public void Account_emails_put_tokens_in_the_fragment_and_encode_the_html()
    {
        var emails = new AccountEmails("https://localhost:7044/");
        var account = new AccountId(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));

        var message = emails.PasswordReset(Guid.NewGuid(), "cook@example.com", account, "a+b/c=");

        const string link = "https://localhost:7044/reset-password#account=6f9619ff-8b86-d011-b42d-00cf4fc964ff&token=a%2Bb%2Fc%3D";
        Assert.Contains(link, message.TextBody, StringComparison.Ordinal);
        Assert.Contains(link.Replace("&", "&amp;", StringComparison.Ordinal), message.HtmlBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("?", link, StringComparison.Ordinal);
        Assert.Contains("1 hour", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_code_notice_says_how_many_codes_are_left()
    {
        var emails = new AccountEmails("https://pantrypress.app");

        Assert.Contains("You have 1 code left.", emails.RecoveryCodeUsed(Guid.NewGuid(), "cook@example.com", 1).TextBody, StringComparison.Ordinal);
        Assert.Contains("You have 9 codes left.", emails.RecoveryCodeUsed(Guid.NewGuid(), "cook@example.com", 9).TextBody, StringComparison.Ordinal);
    }
}
