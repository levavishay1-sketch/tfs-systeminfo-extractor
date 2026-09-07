using System;
using Microsoft.Extensions.Options;

namespace TfsSystemInfoExtractor.Infrastructure.Configuration
{
    public sealed class TfsOptionsValidator : IValidateOptions<TfsOptions>
    {
        public ValidateOptionsResult Validate(string? name, TfsOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.CollectionUrl))
            {
                return ValidateOptionsResult.Fail(
                    $"{TfsOptions.SectionName}:{nameof(TfsOptions.CollectionUrl)} is required " +
                    "(set it in appsettings.json or via the TFS_Tfs__CollectionUrl environment variable).");
            }

            if (!Uri.TryCreate(options.CollectionUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return ValidateOptionsResult.Fail(
                    $"{TfsOptions.SectionName}:{nameof(TfsOptions.CollectionUrl)} must be an absolute http(s) URL.");
            }

            if (string.IsNullOrWhiteSpace(options.ApiVersion))
            {
                return ValidateOptionsResult.Fail($"{TfsOptions.SectionName}:{nameof(TfsOptions.ApiVersion)} is required.");
            }

            if (string.IsNullOrWhiteSpace(options.SystemInfoFieldDisplayName))
            {
                return ValidateOptionsResult.Fail($"{TfsOptions.SectionName}:{nameof(TfsOptions.SystemInfoFieldDisplayName)} is required.");
            }

            if (options.RequestTimeoutSeconds <= 0)
            {
                return ValidateOptionsResult.Fail($"{TfsOptions.SectionName}:{nameof(TfsOptions.RequestTimeoutSeconds)} must be greater than zero.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
