using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class IsolatedKeyLoadAdmissionProfile
{
    private const string Mismatch = "IsolatedKeyLoadAdmissionMismatch";

    internal static void Verify(HttpAdmissionStatus? status, IOptions<HttpAdmissionLimits> admissionOptions)
    {
        ArgumentNullException.ThrowIfNull(admissionOptions);
        var limits = admissionOptions.Value;
        limits.Validate();
        if (status?.Limits != limits)
        {
            throw new ComparisonFailureException(Mismatch);
        }
    }
}
