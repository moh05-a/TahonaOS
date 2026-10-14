using FluentValidation;

namespace Tahona.Application.Features.Auth;

public static class PasswordRules
{
    /// <summary>The one password policy for every place a password is chosen: 8–128 characters with upper case, lower case and a digit.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}
