using Atlas.Domain.Users;
using FluentValidation;

namespace Atlas.Application.Common.Validation;

public static class ValidationRules
{
    public static IRuleBuilderOptionsConditions<T, string> MustBeValidUsername<T>(this IRuleBuilder<T, string> rule) =>
        rule.Custom((value, context) =>
        {
            foreach (var error in UsernamePolicy.Validate(value))
                context.AddFailure(error);
        });

    public static IRuleBuilderOptionsConditions<T, string> MustMeetPasswordPolicy<T>(
        this IRuleBuilder<T, string> rule, Func<T, string?>? usernameSelector = null) =>
        rule.Custom((value, context) =>
        {
            var username = usernameSelector?.Invoke(context.InstanceToValidate);
            foreach (var error in PasswordPolicy.Validate(value, username))
                context.AddFailure(error);
        });
}
