using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminCatalogTests
{
    private const string Administrator = "root";
    private const string Member = "dashboard-member";
    private const string First = "alpha";
    private const string Second = "beta";
    private const string ForeignTenant = "foreign";

    [Test]
    public async Task AcAd004CatalogHasExclusiveContinuationAndNoCrossTenantResources()
    {
        using var db = new TestDatabase();
        db.Configure(Second, ResourceKind.WorkQueue);
        db.Configure(First, ResourceKind.Collection);
        var reader = new AdminCatalogReader(db.Database);
        var request = new AdminResourcesRequest(db.Partition.TenantId, db.Partition.DatabaseId, Limit: 1);
        var first = reader.Read(Administrator, request, CancellationToken.None);
        await Assert.That(first.Items.Length).IsEqualTo(1);
        await Assert.That(first.Items[0].Name).IsEqualTo(First);
        await Assert.That(first.NextAfterName).IsEqualTo(First);
        var next = reader.Read(Administrator, request with { AfterName = first.NextAfterName }, CancellationToken.None);
        await Assert.That(next.Items.Length).IsEqualTo(1);
        await Assert.That(next.Items[0].Name).IsEqualTo(Second);
        await Assert.That(next.NextAfterName).IsNull();
        await Assert.That(next.CutPosition).IsEqualTo(first.CutPosition);
        await Assert.That(reader.Read(Administrator, request with { TenantId = ForeignTenant }, CancellationToken.None).Items).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(AdminDashboardProtocol.MaximumPageSize + 1)]
    public async Task AcAd004InvalidPageBoundIsRejected(int limit)
    {
        using var db = new TestDatabase();
        var request = new AdminResourcesRequest(db.Partition.TenantId, db.Partition.DatabaseId, Limit: limit);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => new AdminCatalogReader(db.Database)
            .Read(Administrator, request, CancellationToken.None));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcAd001OrdinaryPrincipalAndRevokedAdministratorCannotReadCatalog()
    {
        using var db = new TestDatabase();
        var principal = new PrincipalRecord(Member, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, First, Capability.All)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var reader = new AdminCatalogReader(db.Database);
        var request = new AdminResourcesRequest(db.Partition.TenantId, db.Partition.DatabaseId);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => reader.Read(Member, request, CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal with
        { ClusterAdministrator = true, Revoked = true })).Get<PrincipalRecord>();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => reader.Read(Member, request, CancellationToken.None)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    public async Task AcAd004CancelledCatalogReadDoesNotAdvanceAppliedPosition()
    {
        using var db = new TestDatabase();
        db.Configure(First, ResourceKind.Collection);
        var position = db.Database.LastApplied;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => new AdminCatalogReader(db.Database).Read(Administrator,
            new(db.Partition.TenantId, db.Partition.DatabaseId), cancelled.Token));
        await Assert.That(db.Database.LastApplied).IsEqualTo(position);
    }

    [Test]
    public async Task AcAd004RealCatalogFailsClosedWhenReadByteBudgetIsExhausted()
    {
        using var db = new TestDatabase(new() { MaxQueryReadBytes = 1 });
        db.Configure(First, ResourceKind.Collection);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => new AdminCatalogReader(db.Database).Read(Administrator,
            new(db.Partition.TenantId, db.Partition.DatabaseId), CancellationToken.None));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }
}
