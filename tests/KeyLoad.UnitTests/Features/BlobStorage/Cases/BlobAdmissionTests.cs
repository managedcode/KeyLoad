using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-BLOB-004/006: genuine admission retains finite cleanup capacity during data saturation.</summary>
internal sealed class BlobAdmissionTests
{
    private const int ControlCapacity = 2;
    private const int BodyLimit = 1_024;
    private const int ControlBodyLimit = 256;
    private const long ReservedBytes = 20_000;
    private static PrincipalRecord Principal => new(BlobAgentCases.Principal, BlobAgentCases.Partition.TenantId, [], []);

    /// <summary>Abort and reclaim can progress with the actual data lane full, while a third cleanup is rejected.</summary>
    [Test]
    public async Task AcBlob004CleanupUsesTwoBoundedControlReservationsDuringDataSaturation()
    {
        var governor = new HttpAdmissionGovernor(Limits());
        using var data = Begin(governor, BlobAgentCases.PartRoute, 0);
        data.Bind(Principal);
        using var abort = Begin(governor, BlobAgentCases.AbortRoute, ControlBodyLimit);
        abort.Bind(Principal);
        using var reclaim = Begin(governor, BlobAgentCases.ReclaimRoute, ControlBodyLimit);
        reclaim.Bind(Principal);
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(1);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(ControlCapacity);
        await Assert.That(governor.Status().VerifiedScopes.ControlCommands).IsEqualTo(ControlCapacity);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, BlobAgentCases.AbortRoute, 0)).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, BlobAgentCases.BeginRoute, 0)).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        abort.Dispose();
        using var next = Begin(governor, BlobAgentCases.AbortRoute, 0);
        next.Bind(Principal);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(ControlCapacity);
    }

    /// <summary>Cleanup framing and canonical command classification have explicit finite limits.</summary>
    [Test]
    public async Task AcBlob004OnlyAbortAndReclaimUseTheBoundedCleanupLane()
    {
        var governor = new HttpAdmissionGovernor(Limits());
        foreach (var item in BlobAgentCases.All().Where(item => item.CommandKind.HasValue))
        {
            var cleanup = item.Name is BlobAgentCases.Abort or BlobAgentCases.Reclaim;
            await Assert.That(CommandAdmissionGovernor.IsControl(item.CommandKind!.Value)).IsEqualTo(cleanup);
            using var lease = Begin(governor, item.Route, 0);
            await Assert.That(lease.MaxBodyBytes).IsEqualTo(cleanup ? ControlBodyLimit : BodyLimit);
        }
        foreach (var route in new[] { BlobAgentCases.AbortRoute, BlobAgentCases.ReclaimRoute })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, route, ControlBodyLimit + 1)).Code)
                .IsEqualTo(ErrorCode.ResourceExhausted);
        }
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(0);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(0);
    }

    private static HttpAdmissionLease Begin(HttpAdmissionGovernor governor, string route, int bytes) =>
        governor.Begin(route, bytes, TestContext.Current!.Execution.CancellationToken);

    private static HttpAdmissionLimits Limits() => new()
    {
        MaxRequests = 1,
        MaxPrincipalRequests = 1,
        MaxTenantRequests = 1,
        MaxReservedBytes = ReservedBytes,
        ReservedControlBytes = ReservedBytes,
        MaxBodyBytes = BodyLimit,
        MaxControlBodyBytes = ControlBodyLimit,
        ReservedControlRequests = ControlCapacity,
        MaxTenantControlRequests = ControlCapacity,
        MaxPrincipalControlRequests = ControlCapacity,
        OtherReservedBytes = 0,
        HeavyReadReservedBytes = 0
    };
}
