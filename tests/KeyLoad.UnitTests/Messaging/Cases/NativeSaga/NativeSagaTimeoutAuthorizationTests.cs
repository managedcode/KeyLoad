namespace KeyLoad.UnitTests.Features.Messaging;

[NativeSagaTimeoutDataSource]
[NotInParallel]
internal sealed class NativeSagaTimeoutAuthorizationTests(NativeSagaTimeoutFixture fixture)
{
    [Test]
    public async Task PersistedTenantSchedulerDoesNotBorrowClusterAdministratorAuthority()
    {
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(
            view, NativeSagaTimeoutTestData.SagaPrincipalId, TimeProvider.System.GetUtcNow()));
        await Assert.That(principal.TenantId).IsEqualTo(fixture.Database.Partition.TenantId);
        await Assert.That(principal.ClusterAdministrator).IsFalse();
        await Assert.That(principal.Grants.Length).IsEqualTo(2);
        await Assert.That(principal.Grants.All(grant => grant.Database == fixture.Database.Partition.DatabaseId
            && grant.Capabilities == (Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect)))
            .IsTrue();

        var saga = NativeSagaTimeoutTestData.CreateWaitingSaga(fixture,
            NativeSagaTimeoutTestData.SagaPrincipalId, deadlineInFuture: true);
        var visible = fixture.Database.Database.InspectSaga(NativeSagaTimeoutTestData.SagaPrincipalId,
            saga.Lane, saga.Id);
        await Assert.That(visible!.StateJson).IsEqualTo(NativeSagaTimeoutTestData.SagaState);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Database.InspectSaga(
            NativeSagaTimeoutTestData.RootPrincipalId, saga.Lane, saga.Id));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        NativeSagaTimeoutTestData.CompleteSaga(fixture, saga);
    }
}
