using KeyLoad.Core;

namespace KeyLoad.UnitTests;

public sealed class HttpAdmissionTests
{
    private static HttpAdmissionLimits Small() => new()
    {
        MaxRequests = 16, MaxReservedBytes = 100_000, OtherReservedBytes = 0, HeavyReadReservedBytes = 16_384,
        MaxBodyBytes = 1_024, MaxControlBodyBytes = 256, ReservedControlRequests = 2, ReservedControlBytes = 20_000
    };
    private static PrincipalRecord Principal(string id = "p", string tenant = "tenant") => new(id, tenant, [], []);
    private static HttpAdmissionGovernor.Lease Begin(HttpAdmissionGovernor governor, string path, long? bytes = 0)
        => governor.Begin(path, bytes, TestContext.Current.CancellationToken);
    private static void Bind(HttpAdmissionGovernor.Lease lease, PrincipalRecord principal)
        => lease.Bind(principal, TestContext.Current.CancellationToken);
    [Fact]
    public void KnownBodiesAndChunkedBodiesReserveTheirFramingBoundsBeforeAuthentication()
    {
        var governor = new HttpAdmissionGovernor(Small());
        var known = Begin(governor, "/v1/commands", 100); Assert.Equal(4_496, governor.Status().Node.RetainedBytes);
        Assert.Equal(0, governor.Status().VerifiedScopes.Commands);
        known.Dispose(); known.Dispose();
        using var chunked = Begin(governor, "/v1/commands", null);
        Assert.Equal(8_192, governor.Status().Node.RetainedBytes); Assert.Equal(1_024, chunked.MaxBodyBytes);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Begin(governor, "/v1/commands", 1_025)).Code);
        Assert.Equal(1, governor.Status().Node.Commands);
    }
    [Fact]
    public void QuerySearchAndGraphRequestsShareOneWorkingSetReservationBudget()
    {
        var governor = new HttpAdmissionGovernor(Small() with { MaxReservedBytes = 25_000 });
        var query = Begin(governor, "/V1/QUERY/AST/"); Assert.Equal(20_480, governor.Status().Node.RetainedBytes);
        foreach (var path in new[] { "/v1/search", "/v1/graph/traverse", "/v1/changes/read" })
            Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Begin(governor, path)).Code);
        query.Dispose(); using var search = Begin(governor, "/v1/search");
        Assert.Equal(20_480, governor.Status().Node.RetainedBytes);
    }
    [Fact]
    public void OnlyVerifiedBindingsConsumeTenantAndPrincipalScopes()
    {
        var governor = new HttpAdmissionGovernor(Small() with { MaxPrincipalRequests = 1, MaxTenantRequests = 2 });
        using var first = Begin(governor, "/v1/documents/get"); Bind(first, Principal());
        using var unverified = Begin(governor, "/v1/documents/get");
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Bind(unverified, Principal())).Code);
        Assert.Equal(1, governor.Status().VerifiedScopes.Commands);
        using var second = Begin(governor, "/v1/documents/get"); Bind(second, Principal("second"));
        using var third = Begin(governor, "/v1/documents/get");
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Bind(third, Principal("third"))).Code);
        Bind(third, Principal("third", "other"));
        Assert.Equal(2, governor.Status().VerifiedScopes.ActiveTenantScopes); Assert.Equal(3, governor.Status().VerifiedScopes.ActivePrincipalScopes);
        first.Dispose(); second.Dispose(); third.Dispose(); unverified.Dispose();
        Assert.Equal(0, governor.Status().Node.Commands); Assert.Equal(0, governor.Status().VerifiedScopes.ActivePrincipalScopes);
    }
    [Fact]
    public void ControlBodiesHaveAnIndependentBoundWhileProcessingStillUsesDataAdmission()
    {
        var governor = new HttpAdmissionGovernor(Small() with { MaxRequests = 1, MaxPrincipalRequests = 1 });
        using var data = Begin(governor, "/v1/queues/process"); Bind(data, Principal());
        using var control = Begin(governor, "/V1/QUEUES/DELIVERY/", 256); Bind(control, Principal());
        Assert.Equal(256, control.MaxBodyBytes); Assert.Equal(1, governor.Status().Node.ControlCommands);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Begin(governor, "/v1/subscriptions/process")).Code);
        Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => Begin(governor, "/v1/subscriptions/delivery", 257)).Code);
        data.Dispose(); control.Dispose(); Assert.Equal(0, governor.Status().Node.ControlRetainedBytes);
        Assert.Equal(0, governor.Status().VerifiedScopes.ControlCommands);
    }
    [Fact]
    public void CancellationAndRejectedBindingsReleaseEveryReservationAtRequestEnd()
    {
        var governor = new HttpAdmissionGovernor(Small()); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => governor.Begin("/v1/commands", 0, cancelled.Token));
        var lease = Begin(governor, "/v1/commands");
        Assert.Throws<OperationCanceledException>(() => lease.Bind(Principal(), cancelled.Token));
        Assert.Equal(1, governor.Status().Node.Commands); Assert.Equal(0, governor.Status().VerifiedScopes.Commands);
        Bind(lease, Principal()); Assert.Throws<InvalidOperationException>(() => Bind(lease, Principal("different")));
        lease.Dispose(); Assert.Throws<ObjectDisposedException>(() => Bind(lease, Principal()));
        Assert.Equal(0, governor.Status().Node.RetainedBytes); Assert.Equal(0, governor.Status().VerifiedScopes.ActiveTenantScopes);
    }
}
