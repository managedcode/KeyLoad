using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class NativeDocumentPolicy
{
    private const string DurationFormat = "c";
    internal static void Validate(NativeComparisonExecutionOptions options)
    {
        if (options.DocumentClientMaxConnections != NativeComparisonExecutionOptions.RequiredDocumentClientConnections
            || options.DocumentVerificationConnectionReserve != NativeComparisonExecutionOptions.RequiredDocumentClientConnections
            || options.DocumentCleanupTimeout != NativeComparisonExecutionOptions.RequiredDocumentCleanupTimeout)
        {
            throw new OptionsValidationException(NativeComparisonExecutionOptions.SectionName, typeof(NativeComparisonExecutionOptions), [DocumentProtocolText.DocumentContractInvalid]);
        }
    }
    internal static void Record(NativeComparisonExecutionOptions options, IDictionary<string, string> parameters)
    {
        parameters[nameof(options.DocumentClientMaxConnections)] = options.DocumentClientMaxConnections.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.DocumentVerificationConnectionReserve)] = options.DocumentVerificationConnectionReserve.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(options.DocumentCleanupTimeout)] = options.DocumentCleanupTimeout.ToString(DurationFormat, CultureInfo.InvariantCulture);
    }
}
