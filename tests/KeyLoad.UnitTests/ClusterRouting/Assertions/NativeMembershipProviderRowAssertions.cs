using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeMembershipProviderRowAssertions
{
    private const int SingleRow = 1;

    internal static async Task CurrentAsync(NativeMembershipProviderObservation observation,
        SiloHandle[] handles, NativeMembershipProviderObservation? before)
    {
        await Assert.That(observation.Rows.Count(row => row.Status == SiloStatus.Active)).IsEqualTo(handles.Length);
        foreach (var handle in handles)
        {
            var matches = observation.Rows.Where(row => row.SiloAddress.Equals(handle.SiloAddress)).ToArray();
            await Assert.That(matches.Length).IsEqualTo(SingleRow);
            var row = matches.Single();
            await Assert.That(row.SiloName).IsEqualTo(handle.Name);
            await Assert.That(row.Status).IsEqualTo(SiloStatus.Active);
            if (before is not null)
            {
                var previous = before.Rows.Single(item => item.SiloAddress.Equals(handle.SiloAddress));
                await StableAsync(row, previous).ConfigureAwait(false);
            }
        }
    }

    internal static async Task SingleAsync(NativeMembershipProviderObservation observation,
        SiloHandle handle, NativeMembershipProviderObservation all)
    {
        await Assert.That(observation.Rows.Length).IsEqualTo(SingleRow);
        var row = observation.Rows.Single();
        await Assert.That(row.SiloAddress.Equals(handle.SiloAddress)).IsTrue();
        await Assert.That(row.SiloName).IsEqualTo(handle.Name);
        await Assert.That(row.Status).IsEqualTo(SiloStatus.Active);
        await StableAsync(row, all.Rows.Single(item => item.SiloAddress.Equals(handle.SiloAddress))).ConfigureAwait(false);
    }

    internal static async Task OldGenerationAsync(NativeMembershipProviderObservation observation,
        SiloAddress oldAddress)
    {
        await Assert.That(observation.Rows.Any(row => row.SiloAddress.Equals(oldAddress)
            && row.Status == SiloStatus.Active)).IsFalse();
    }

    private static async Task StableAsync(NativeMembershipProviderRow actual, NativeMembershipProviderRow expected)
    {
        await Assert.That(actual.SiloAddress.Equals(expected.SiloAddress)).IsTrue();
        await Assert.That(actual.SiloName).IsEqualTo(expected.SiloName);
        await Assert.That(actual.HostName).IsEqualTo(expected.HostName);
        await Assert.That(actual.RoleName).IsEqualTo(expected.RoleName);
        await Assert.That(actual.ProxyPort).IsEqualTo(expected.ProxyPort);
        await Assert.That(actual.FaultZone).IsEqualTo(expected.FaultZone);
        await Assert.That(actual.UpdateZone).IsEqualTo(expected.UpdateZone);
        await Assert.That(actual.StartTime).IsEqualTo(expected.StartTime);
    }
}
