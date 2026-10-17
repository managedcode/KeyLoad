using System.Text;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipReadiness
{
    private const int ExpectedActiveSilos = 6;
    private const int SnapshotVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task<MembershipReadinessSnapshot?> ReadNodeAsync(bool siloJoined, IServiceProvider? services,
        IOptions<NodeOptions> nodeOptions, IOptions<OrleansMembershipOptions> membershipOptions, CancellationToken token)
    {
        if (!siloJoined || services is null)
        {
            return null;
        }
        try
        {
            return await ReadAsync(services.GetRequiredService<ReplicaNativeDiscoveryRuntime>(), nodeOptions,
                services.GetRequiredService<ILocalSiloDetails>().SiloAddress, membershipOptions, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return null;
        }
    }

    private static Task<MembershipReadinessSnapshot?> ReadAsync(ReplicaNativeDiscoveryRuntime runtime,
        IOptions<NodeOptions> nodeOptions, SiloAddress localAddress,
        IOptions<OrleansMembershipOptions> membershipOptions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var view = runtime.ReadActiveMembership();
        return Task.FromResult(ReadView(view, nodeOptions.Value, localAddress, membershipOptions.Value, token));
    }

    internal static MembershipReadinessSnapshot? ReadView(
        System.Collections.Immutable.ImmutableArray<ReplicaNativeMembershipEntry> view, NodeOptions options,
        SiloAddress localAddress, OrleansMembershipOptions bounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bounds.Validate();
        if (view.IsDefault || view.Length != ExpectedActiveSilos || view.Length > bounds.MaximumRows)
        {
            return null;
        }
        var expected = ExpectedNames(options, bounds);
        if (expected is null)
        {
            return null;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var local = localAddress.ToParsableString();
        foreach (var entry in view)
        {
            token.ThrowIfCancellationRequested();
            if (!Bounded(entry.SiloName, bounds) || !Bounded(entry.SiloAddress, bounds)
                || !expected.Contains(entry.SiloName) || !names.Add(entry.SiloName)
                || !addresses.Add(entry.SiloAddress)
                || SiloAddress.FromParsableString(entry.SiloAddress).ToParsableString() != entry.SiloAddress)
            {
                return null;
            }
        }
        token.ThrowIfCancellationRequested();
        if (!names.SetEquals(expected) || !addresses.Contains(local))
        {
            return null;
        }
        return new(SnapshotVersion, view.Length, view.Length, ReplicaMembershipFingerprint.Compute(addresses));
    }

    private static HashSet<string>? ExpectedNames(NodeOptions options, OrleansMembershipOptions bounds)
    {
        const int GroupVoters = 3;
        if (options.Peers.Count != GroupVoters || options.PhysicalGroupTrust.ForeignVoterIds.Length != GroupVoters)
        {
            return null;
        }
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in options.Peers.Concat(options.PhysicalGroupTrust.ForeignVoterIds))
        {
            if (!Bounded(name, bounds) || !expected.Add(name))
            {
                return null;
            }
        }
        return expected;
    }

    private static bool Bounded(string value, OrleansMembershipOptions bounds)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(value) <= bounds.MaximumAddressBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
