namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class HttpAdmissionGovernorTests
{
    private static HttpAdmissionLimits Small() => new()
    {
        MaxRequests = 16,
        MaxReservedBytes = 100_000,
        OtherReservedBytes = 0,
        HeavyReadReservedBytes = 16_384,
        MaxBodyBytes = 1_024,
        MaxControlBodyBytes = 256,
        ReservedControlRequests = 2,
        ReservedControlBytes = 20_000
    };
    private static PrincipalRecord Principal(string id = "p", string tenant = "tenant") => new(id, tenant, [], []);
    private static HttpAdmissionLease Begin(HttpAdmissionGovernor governor, string path, long? bytes = 0)
        => governor.Begin(path, bytes, TestContext.Current!.Execution.CancellationToken);
    private static void Bind(HttpAdmissionLease lease, PrincipalRecord principal)
        => lease.Bind(principal, TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task KnownBodiesAndChunkedBodiesReserveTheirFramingBoundsBeforeAuthentication()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(Small()));
        var known = Begin(governor, "/commands", 100);
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(4_496);
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(0);
        known.Dispose();
        known.Dispose();
        using var chunked = Begin(governor, "/commands", null);
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(8_192);
        await Assert.That(chunked.MaxBodyBytes).IsEqualTo(1_024);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, "/commands", 1_025)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(1);
    }
    [Test]
    public async Task QuerySearchAndGraphRequestsShareOneWorkingSetReservationBudget()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(Small() with
        {
            MaxReservedBytes = 25_000
        }));
        var query = Begin(governor, "/QUERY/AST/");
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(20_480);
        var paths = new[] { "/search", "/graph/traverse", "/changes/read",
            "/search/graph", AnnSearchProtocol.Route, "/graph/shortest-path", "/graph/incoming",
            SampleChunkProtocol.ReadRoute, SampleRollupProtocol.ReadRoute,
            "/SERIES/CHUNKS/WINDOW/", "/SERIES/ROLLUPS/READ/" };
        foreach (var path in paths)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, path)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }

        query.Dispose();
        foreach (var path in paths)
        {
            var admitted = Begin(governor, path);
            Bind(admitted, Principal());
            await Assert.That(governor.Status().Node)
                .IsEqualTo(new CommandAdmissionSnapshot(1, 20_480, 0, 0, 1, 1));
            await Assert.That(governor.Status().VerifiedScopes)
                .IsEqualTo(new CommandAdmissionSnapshot(1, 4_096, 0, 0, 1, 1));
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, "/query/ast")).Code)
                .IsEqualTo(ErrorCode.ResourceExhausted);
            admitted.Dispose();
            admitted.Dispose();
            await Assert.That(governor.Status().Node)
                .IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
            await Assert.That(governor.Status().VerifiedScopes)
                .IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        }
        using var search = Begin(governor, "/search");
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(20_480);
    }
    [Test]
    public async Task OnlyVerifiedBindingsConsumeTenantAndPrincipalScopes()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(Small() with
        {
            MaxPrincipalRequests = 1,
            MaxTenantRequests = 2
        }));
        using var first = Begin(governor, "/documents/get");
        Bind(first, Principal());
        using var unverified = Begin(governor, "/documents/get");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Bind(unverified, Principal())).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(1);
        using var second = Begin(governor, "/documents/get");
        Bind(second, Principal("second"));
        using var third = Begin(governor, "/documents/get");
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Bind(third, Principal("third"))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        Bind(third, Principal("third", "other"));
        await Assert.That(governor.Status().VerifiedScopes.ActiveTenantScopes).IsEqualTo(2);
        await Assert.That(governor.Status().VerifiedScopes.ActivePrincipalScopes).IsEqualTo(3);
        first.Dispose();
        second.Dispose();
        third.Dispose();
        unverified.Dispose();
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(0);
        await Assert.That(governor.Status().VerifiedScopes.ActivePrincipalScopes).IsEqualTo(0);
    }
    [Test]
    public async Task ControlBodiesHaveAnIndependentBoundWhileProcessingStillUsesDataAdmission()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(Small() with
        {
            MaxRequests = 1,
            MaxPrincipalRequests = 1
        }));
        using var data = Begin(governor, "/queues/process");
        Bind(data, Principal());
        using var control = Begin(governor, "/QUEUES/DELIVERY/", 256);
        Bind(control, Principal());
        await Assert.That(control.MaxBodyBytes).IsEqualTo(256);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(1);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, "/subscriptions/process")).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Begin(governor, "/subscriptions/delivery", 257)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        data.Dispose();
        control.Dispose();
        await Assert.That(governor.Status().Node.ControlRetainedBytes).IsEqualTo(0);
        await Assert.That(governor.Status().VerifiedScopes.ControlCommands).IsEqualTo(0);
    }
    [Test]
    public async Task CancellationAndRejectedBindingsReleaseEveryReservationAtRequestEnd()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http(Small()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => governor.Begin("/commands", 0, cancelled.Token));
        var lease = Begin(governor, "/commands");
        Assert.ThrowsExactly<OperationCanceledException>(() => lease.Bind(Principal(), cancelled.Token));
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(1);
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(0);
        Bind(lease, Principal());
        Assert.ThrowsExactly<InvalidOperationException>(() => Bind(lease, Principal("different")));
        lease.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => Bind(lease, Principal()));
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(0);
        await Assert.That(governor.Status().VerifiedScopes.ActiveTenantScopes).IsEqualTo(0);
    }
}
