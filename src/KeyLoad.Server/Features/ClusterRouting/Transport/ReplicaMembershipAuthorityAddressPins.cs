using System.Net;
using System.Net.Sockets;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityAddressPins : IDisposable
{
    private const int MaximumAddresses = 8;
    private readonly string[] hosts;
    private readonly SemaphoreSlim[] gates;
    private readonly IPAddress[]?[] pins;

    internal ReplicaMembershipAuthorityAddressPins(string[] configuredEndpoints)
    {
        ArgumentNullException.ThrowIfNull(configuredEndpoints);
        if (configuredEndpoints.Length != MembershipAuthoritySettingsProtocol.RequiredMembers)
        { throw new ArgumentException(MembershipAuthoritySettingsProtocol.Invalid); }
        hosts = configuredEndpoints.Select(endpoint => endpoint[..endpoint.LastIndexOf(':')]).ToArray();
        gates = Enumerable.Range(0, hosts.Length).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
        pins = new IPAddress[hosts.Length][];
    }

    internal async Task PinCallerAsync(int voterIndex, IPAddress callerAddress, CancellationToken cancellationToken)
    {
        if ((uint)voterIndex >= (uint)hosts.Length)
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidIdentity); }
        ArgumentNullException.ThrowIfNull(callerAddress);
        await gates[voterIndex].WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var addresses = pins[voterIndex] ?? await ResolveAsync(voterIndex, cancellationToken).ConfigureAwait(false);
            if (!addresses.Contains(callerAddress))
            { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidIdentity); }
            pins[voterIndex] ??= addresses;
        }
        finally { gates[voterIndex].Release(); }
    }

    private async Task<IPAddress[]> ResolveAsync(int voterIndex, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        { addresses = await Dns.GetHostAddressesAsync(hosts[voterIndex], cancellationToken).ConfigureAwait(false); }
        catch (SocketException)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        cancellationToken.ThrowIfCancellationRequested();
        if (addresses.Length == 0)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        if (addresses.Length > MaximumAddresses)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        if (addresses.Any(address => address is null || address.GetAddressBytes().Length is not 4 and not 16)
            || addresses.Distinct().Count() != addresses.Length)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        return [.. addresses];
    }

    public void Dispose()
    {
        foreach (var gate in gates) { gate.Dispose(); }
    }
}
