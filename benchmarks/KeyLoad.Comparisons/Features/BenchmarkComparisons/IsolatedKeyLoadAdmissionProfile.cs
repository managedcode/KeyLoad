namespace KeyLoad.Comparisons;

internal static class IsolatedKeyLoadAdmissionProfile
{
    private const string Mismatch = "IsolatedKeyLoadAdmissionMismatch";
    internal static HttpAdmissionLimits Limits { get; } = new()
    {
        MaxRequests = 32,
        MaxTenantRequests = 32,
        MaxPrincipalRequests = 32,
        ReservedControlRequests = 32,
        MaxTenantControlRequests = 32,
        MaxPrincipalControlRequests = 32,
        MaxReservedBytes = 2_147_483_648,
    };

    internal static void Verify(HttpAdmissionStatus? status)
    {
        if (status?.Limits != Limits)
        {
            throw new ComparisonFailureException(Mismatch);
        }
    }
}
