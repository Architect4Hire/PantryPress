using FluentValidation;
using PantryPress.Domain.Modules.Accounts.Business;
using PantryPress.Domain.Modules.Accounts.Contracts;

namespace PantryPress.Domain.Modules.Accounts.Validation;

// Facade-edge validation for the account requests (backend.md). Messages are plain language and never mention whether
// an account exists. Tokens and codes are bounded so a huge value is rejected before any cryptography runs.

internal static class AccountValidationRules
{
    // Data-protected tokens are a few hundred characters; anything far longer is not one of ours.
    public const int MaxTokenLength = 1024;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter your email address.")
            .Must(EmailAddressRules.IsValid).WithMessage("Enter an email address like name@example.com.");

    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(PasswordRules.MinLength)
            .WithMessage($"Use at least {PasswordRules.MinLength} characters. A short phrase is easy to remember.")
            .MaximumLength(PasswordRules.MaxLength)
            .WithMessage($"Use {PasswordRules.MaxLength} characters or fewer.");

    public static IRuleBuilderOptions<T, string> CurrentPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Enter your password.")
            .MaximumLength(PasswordRules.MaxLength).WithMessage("That password isn't right.");

    public static IRuleBuilderOptions<T, string> LinkToken<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("The link is missing a part. Open it again from the email.")
            .MaximumLength(MaxTokenLength).WithMessage("The link is missing a part. Open it again from the email.");

    public static IRuleBuilderOptions<T, Guid> LinkAccount<T>(this IRuleBuilder<T, Guid> rule) =>
        rule.NotEmpty().WithMessage("The link is missing a part. Open it again from the email.");
}

internal sealed class RegisterAccountRequestValidator : AbstractValidator<RegisterAccountRequest>
{
    public RegisterAccountRequestValidator()
    {
        RuleFor(r => r.Email).Cascade(CascadeMode.Stop)
            .ValidEmail()
            .Must(email => !DisposableEmailDomains.IsDisposable(email))
            .WithMessage("Please use an email address you'll keep. Temporary inboxes can't be used for PantryPress.");
        RuleFor(r => r.Password).Cascade(CascadeMode.Stop).NewPassword();
    }
}

internal sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.AccountId).LinkAccount();
        RuleFor(r => r.Token).Cascade(CascadeMode.Stop).LinkToken();
    }
}

internal sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator() => RuleFor(r => r.Email).Cascade(CascadeMode.Stop).ValidEmail();
}

internal sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Email).Cascade(CascadeMode.Stop).ValidEmail();
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.AccountId).LinkAccount();
        RuleFor(r => r.Token).Cascade(CascadeMode.Stop).LinkToken();
        RuleFor(r => r.NewPassword).Cascade(CascadeMode.Stop).NewPassword();
    }
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).Cascade(CascadeMode.Stop).CurrentPassword();
        RuleFor(r => r.NewPassword).Cascade(CascadeMode.Stop).NewPassword();
    }
}

internal sealed class ConfirmPasswordRequestValidator : AbstractValidator<ConfirmPasswordRequest>
{
    public ConfirmPasswordRequestValidator() => RuleFor(r => r.Password).Cascade(CascadeMode.Stop).CurrentPassword();
}

internal sealed class EnableTwoFactorRequestValidator : AbstractValidator<EnableTwoFactorRequest>
{
    public EnableTwoFactorRequestValidator() =>
        RuleFor(r => r.Code).Must(code => Totp.TryNormalize(code, out _))
            .WithMessage("Enter the 6-digit code from your authenticator app.");
}

internal sealed class SecondFactorRequestValidator : AbstractValidator<SecondFactorRequest>
{
    public SecondFactorRequestValidator() =>
        RuleFor(r => r.Code).Must(code => Totp.TryNormalize(code, out _) || RecoveryCodes.Normalize(code) is not null)
            .WithMessage("Enter the 6-digit code from your authenticator app, or one of your recovery codes.");
}

internal sealed class SignInRequestValidator : AbstractValidator<SignInRequest>
{
    // No disposable-domain rule: that one belongs to sign-up. An address that already has an account signs in.
    public SignInRequestValidator()
    {
        RuleFor(r => r.Email).Cascade(CascadeMode.Stop).ValidEmail();
        RuleFor(r => r.Password).Cascade(CascadeMode.Stop).CurrentPassword();
    }
}
