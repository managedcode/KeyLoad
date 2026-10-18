using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadAdmissionObserver
{
    private const string ObserveAsyncHTTPAdmissionText = "HTTP admission: ";
    private const string ObserveAsyncNodeText = " node / ";
    private const string ObserveAsyncTenantText = " tenant / ";
    private const string ObserveAsyncPrincipalDataRequestsText = " principal data requests; ";
    private const string ObserveAsyncDataBytesText = " data bytes; ";
    private const string ObserveAsyncPrincipalControlRequestsMemberEndpointText = " principal control requests; member endpoint ";

    internal static async Task<string[]> ObserveAsync(bool required, HttpClient[] peers, string credential,
        IOptions<KeyLoadClientExecutionOptions> clientOptions, IOptions<HttpAdmissionLimits> admissionOptions, CancellationToken cancellationToken)
    {
        const string AdmissionToken = "Admission";

        if (!required)
        {
            return [];
        }
        var observations = new List<string>(peers.Length);
        foreach (var peer in peers)
        {
            var reader = new KeyLoadClient(peer, credential, clientOptions);
            var status = KeyLoadClientResults.Success(await reader.AdmissionStatusAsync(cancellationToken), AdmissionToken);
            IsolatedKeyLoadAdmissionProfile.Verify(status.Http, admissionOptions);
            var limits = status.Http!.Limits;
            observations.Add($"{ObserveAsyncHTTPAdmissionText}{limits.MaxRequests}{ObserveAsyncNodeText}{limits.MaxTenantRequests}{ObserveAsyncTenantText}"
                + $"{limits.MaxPrincipalRequests}{ObserveAsyncPrincipalDataRequestsText}{limits.MaxReservedBytes}{ObserveAsyncDataBytesText}"
                + $"{limits.ReservedControlRequests}{ObserveAsyncNodeText}{limits.MaxTenantControlRequests}{ObserveAsyncTenantText}"
                + $"{limits.MaxPrincipalControlRequests}{ObserveAsyncPrincipalControlRequestsMemberEndpointText}{peer.BaseAddress}");
        }
        return observations.ToArray();
    }
}
