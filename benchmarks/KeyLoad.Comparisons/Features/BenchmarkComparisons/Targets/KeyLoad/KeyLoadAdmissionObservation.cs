using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    internal bool RequireIsolatedAdmission { get; init; }
    private string[] admissionObservations = [];

    private async Task ObserveAdmissionAsync(CancellationToken cancellationToken)
    {
        if (!RequireIsolatedAdmission)
        {
            return;
        }
        var observations = new List<string>(peerClients.Length);
        foreach (var peer in peerClients)
        {
            var reader = new KeyLoadClient(peer, credential);
            var status = KeyLoadClientResults.Success(await reader.AdmissionStatusAsync(cancellationToken), "Admission");
            IsolatedKeyLoadAdmissionProfile.Verify(status.Http);
            var limits = status.Http!.Limits;
            observations.Add($"HTTP admission: {limits.MaxRequests} node / {limits.MaxTenantRequests} tenant / "
                + $"{limits.MaxPrincipalRequests} principal data requests; {limits.MaxReservedBytes} data bytes; "
                + $"{limits.ReservedControlRequests} node / {limits.MaxTenantControlRequests} tenant / "
                + $"{limits.MaxPrincipalControlRequests} principal control requests; member endpoint {peer.BaseAddress}");
        }
        admissionObservations = observations.ToArray();
    }
}
