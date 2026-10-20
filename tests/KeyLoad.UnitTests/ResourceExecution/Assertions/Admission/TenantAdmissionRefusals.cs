namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionRefusals
{
    internal static async Task RunAsync(TestDatabase fixture, CommandAdmissionGovernor governor, CancellationToken token)
    {
        var state = TenantAdmissionAssertions.State(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var charges = governor.Snapshot();
        var excessive = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "refused");
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            TenantAdmissionOperations.Reserve(fixture, governor, excessive, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
        await selected.CancelAsync();
        var cancelled = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, "cancelled");
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            TenantAdmissionOperations.Reserve(fixture, governor, cancelled, selected.Token));
        await Assert.That(governor.Snapshot()).IsEqualTo(charges);
        await TenantAdmissionAssertions.UnchangedAsync(fixture, state, cut);
        await Assert.That(fixture.Database.GetDocument(TenantAdmissionOperations.PrincipalA,
            new(fixture.Partition, TenantAdmissionOperations.Collection, "refused"), cancellationToken: token)).IsNull();
        await Assert.That(fixture.Database.GetDocument(TenantAdmissionOperations.PrincipalB,
            new(TenantAdmissionOperations.PartitionB(fixture), TenantAdmissionOperations.Collection, "cancelled"), cancellationToken: token))
            .IsNull();
    }
}
