using System.Net;
using System.Net.Sockets;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityAddressPins : IDisposable
{
    private readonly OrleansMembershipOptions settings;
    private readonly string[] hosts;
    private readonly SemaphoreSlim[] gates;
    private readonly IPAddress[]?[] pins;

    internal ReplicaMembershipAuthorityAddressPins(string[] configuredEndpoints, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const char ColonCharacter = ':';
        const int StartEmptyCount = 0;
        const int VoterPinMutexPermits = 1;

        ArgumentNullException.ThrowIfNull(membershipOptions);
        settings = membershipOptions.Value;
        settings.Validate();
        ArgumentNullException.ThrowIfNull(configuredEndpoints);
        if (configuredEndpoints.Length != MembershipAuthoritySettingsProtocol.RequiredMembers)
        { throw new ArgumentException(MembershipAuthoritySettingsProtocol.Invalid); }
        hosts = configuredEndpoints.Select(endpoint => endpoint[..endpoint.LastIndexOf(ColonCharacter)]).ToArray();
        gates = Enumerable.Range(StartEmptyCount, hosts.Length).Select(_ => new SemaphoreSlim(VoterPinMutexPermits, VoterPinMutexPermits)).ToArray();
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
        const int EmptyAddressesLength = 0;
        const int Ipv4AddressBytes = 4;
        const int Ipv6AddressBytes = 16;

        IPAddress[] addresses;
        try
        { addresses = await Dns.GetHostAddressesAsync(hosts[voterIndex], cancellationToken).ConfigureAwait(false); }
        catch (SocketException)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        cancellationToken.ThrowIfCancellationRequested();
        if (addresses.Length == EmptyAddressesLength)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        if (addresses.Length > settings.MaximumResolvedAddresses)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.MembershipCapacity); }
        if (addresses.Any(address => address is null || address.GetAddressBytes().Length is not Ipv4AddressBytes and not Ipv6AddressBytes)
            || addresses.Distinct().Count() != addresses.Length)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable); }
        return [.. addresses];
    }

    public void Dispose()
    {
        foreach (var gate in gates)
        { gate.Dispose(); }
    }
}
