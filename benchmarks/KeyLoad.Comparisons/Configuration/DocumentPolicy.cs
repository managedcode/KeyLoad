using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class DocumentPolicy
{
    private const string DurationFormat = "c";
    internal static void Validate(ComparisonExecutionOptions options)
    {
        if (options.DocumentClientMaxConnections != ComparisonExecutionOptions.RequiredDocumentClientConnections
            || options.DocumentClientMaxResponseHeadersKilobytes != ComparisonExecutionOptions.RequiredDocumentClientResponseHeadersKilobytes
            || options.DocumentVerificationConnectionReserve != ComparisonExecutionOptions.RequiredDocumentClientConnections
            || options.DocumentCleanupTimeout != ComparisonExecutionOptions.RequiredDocumentCleanupTimeout)
        {
            throw new OptionsValidationException(ComparisonExecutionOptions.SectionName, typeof(ComparisonExecutionOptions), [DocumentProtocolText.DocumentContractInvalid]);
        }
    }
    internal static void Record(ComparisonExecutionOptions options, IDictionary<string, string> parameters)
    {
        parameters[nameof(options.DocumentClientMaxConnections)] = options.DocumentClientMaxConnections.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.DocumentClientMaxResponseHeadersKilobytes)] = options.DocumentClientMaxResponseHeadersKilobytes.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.DocumentVerificationConnectionReserve)] = options.DocumentVerificationConnectionReserve.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.DocumentCleanupTimeout)] = options.DocumentCleanupTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
    }
}
