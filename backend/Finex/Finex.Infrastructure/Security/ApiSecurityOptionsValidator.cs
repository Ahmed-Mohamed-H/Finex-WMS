using Microsoft.Extensions.Options;

namespace Finex.Infrastructure.Security
{
    public sealed class ApiSecurityOptionsValidator : IValidateOptions<ApiSecurityOptions>
    {
        public ValidateOptionsResult Validate(string? name, ApiSecurityOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.CurrentSecret) || options.CurrentSecret.Length < 32)
            {
                return ValidateOptionsResult.Fail(
                    $"{ApiSecurityOptions.SectionName}:CurrentSecret must be configured and at least 32 characters long.");
            }

            if (!string.IsNullOrEmpty(options.PreviousSecret) && options.PreviousSecret.Length < 32)
            {
                return ValidateOptionsResult.Fail(
                    $"{ApiSecurityOptions.SectionName}:PreviousSecret must be at least 32 characters long when configured.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}