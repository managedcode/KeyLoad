using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeMembershipProviderRows
{
    internal static async Task<NativeMembershipProviderObservation> ReadAsync(
        TestCluster cluster, SiloAddress reader, CancellationToken token)
    {
        var provider = cluster.GetSiloServiceProvider(reader).GetRequiredService<IMembershipTable>();
        var data = await provider.ReadAllAsync(token).ConfigureAwait(false);
        return Capture(data);
    }

    internal static async Task RequireAsync(TestCluster cluster, SiloHandle[] handles,
        NativeMembershipProviderObservation? before, CancellationToken token)
    {
        var reader = handles.First().SiloAddress;
        var provider = cluster.GetSiloServiceProvider(reader).GetRequiredService<IMembershipTable>();
        var all = await ReadAsync(cluster, reader, token).ConfigureAwait(false);
        await NativeMembershipProviderRowAssertions.CurrentAsync(all, handles, before).ConfigureAwait(false);
        foreach (var handle in handles)
        {
            var data = await provider.ReadRowAsync(handle.SiloAddress, token).ConfigureAwait(false);
            var row = Capture(data);
            await NativeMembershipProviderRowAssertions.SingleAsync(row, handle, all).ConfigureAwait(false);
        }
    }

    internal static async Task RestartedAsync(TestCluster cluster, SiloHandle[] handles,
        SiloAddress oldAddress, CancellationToken token)
    {
        var all = await ReadAsync(cluster, handles.First().SiloAddress, token).ConfigureAwait(false);
        await NativeMembershipProviderRowAssertions.OldGenerationAsync(all, oldAddress).ConfigureAwait(false);
        await RequireAsync(cluster, handles, null, token).ConfigureAwait(false);
    }

    private static NativeMembershipProviderObservation Capture(MembershipTableData data)
    {
        var rows = data.Members.Select(item =>
        {
            var entry = item.Item1;
            return new NativeMembershipProviderRow(entry.SiloAddress, entry.SiloName, entry.HostName,
                entry.RoleName, entry.ProxyPort, entry.FaultZone, entry.UpdateZone, entry.StartTime,
                entry.Status, entry.IAmAliveTime, entry.SuspectTimes?.ToArray(), item.Item2);
        }).ToArray();
        return new NativeMembershipProviderObservation(rows, data.Version.Version, data.Version.VersionEtag);
    }
}
