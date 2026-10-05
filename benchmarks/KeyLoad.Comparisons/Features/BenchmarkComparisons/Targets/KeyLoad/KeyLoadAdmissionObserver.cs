using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadAdmissionObserver
{
    internal static async Task<string[]> ObserveAsync(bool required, HttpClient[] peers, string credential,
        CancellationToken cancellationToken, IOptions<KeyLoadClientExecutionOptions> clientOptions)
    {
        if (!required)
        {
            return [];
        }
        var observations = new List<string>(peers.Length);
        foreach (var peer in peers)
        {
            var reader = new KeyLoadClient(peer, credential, clientOptions);
            var status = KeyLoadClientResults.Success(await reader.AdmissionStatusAsync(cancellationToken), "Admission");
            IsolatedKeyLoadAdmissionProfile.Verify(status.Http);
            var limits = status.Http!.Limits;
            observations.Add($"HTTP admission: {limits.MaxRequests} node / {limits.MaxTenantRequests} tenant / "
                + $"{limits.MaxPrincipalRequests} principal data requests; {limits.MaxReservedBytes} data bytes; "
                + $"{limits.ReservedControlRequests} node / {limits.MaxTenantControlRequests} tenant / "
                + $"{limits.MaxPrincipalControlRequests} principal control requests; member endpoint {peer.BaseAddress}");
        }
        return observations.ToArray();
    }
}
