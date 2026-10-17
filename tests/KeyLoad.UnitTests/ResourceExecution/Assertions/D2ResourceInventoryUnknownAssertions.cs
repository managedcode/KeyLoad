using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryUnknownAssertions
{
    internal static async Task RefusedAsync(D2ResourceInventoryFixture fixture, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var work = fixture.Work(token);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
        {
            var request = fixture.Database.AdmitResourceOwnerInventoryRequest(D2ResourceInventoryData.Administrator, Guid.NewGuid(),
                D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, D2ResourceInventoryData.A,
                D2ResourceInventoryData.PageSize,
                fixture.Database.EvaluationClock.GetUtcNow().AddSeconds(fixture.Database.Limits.QueryDeadlineSeconds), work);
            var control = fixture.Database.CaptureResourceOwnerInventoryControl(
                D2ResourceInventoryData.Administrator, request, work);
            var observed = control.Directory.Owners.Single(entry =>
                entry.Owner.PhysicalShardId == D2ResourceInventoryData.Control.Owner.PhysicalShardId);
            _ = fixture.Page(control, observed, null, work);
        }, ErrorCode.RecoveryRequired);
        await before.AssertUnchangedAsync(fixture.Owner);
    }

    internal static async Task ReplayAsync(D2ResourceInventoryFixture fixture,
        IReadOnlyList<(ReplicatedOperation Operation, OperationResult Outcome)> writes, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var ids = new[] { D2ResourceInventoryData.DocumentId, D2ResourceInventoryUnknownSeed.FollowupId };
        await Assert.That(writes.Count).IsEqualTo(ids.Length);
        for (var index = 0; index < writes.Count; index++)
        {
            var original = writes[index];
            var receipt = original.Outcome.Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(original.Operation.Id);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(D2ResourceInventoryData.Partitions[0].AtomicPartitionId);
            var repeated = fixture.Submit(original.Operation, token);
            await D2ResourceInventoryAssertions.EqualAsync(original.Outcome, repeated);
            await D2ResourceInventoryAssertions.EqualAsync(receipt, repeated.Get<CommitReceipt>());
            var document = fixture.Database.GetDocument(D2ResourceInventoryData.Root,
                new(D2ResourceInventoryData.Partitions[0], D2ResourceInventoryData.A, ids[index]), cancellationToken: token);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Json).IsEqualTo(D2ResourceInventoryData.Json[index]);
            await Assert.That(document.Revision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        }
        await Assert.That(fixture.Database.Authenticate(D2ResourceInventoryUnknownSeed.Credential,
            fixture.Database.EvaluationClock.GetUtcNow())).IsEqualTo(D2ResourceInventoryUnknownSeed.SecondAdministrator);
        var administrator = fixture.Owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(D2ResourceInventoryUnknownSeed.SecondAdministrator)));
        await Assert.That(administrator).IsNotNull();
        await Assert.That(administrator!.ClusterAdministrator).IsTrue();
        await Assert.That(administrator.PolicyEpoch).IsEqualTo(D2ResourceInventoryData.EmptyRevision);
        await before.AssertUnchangedAsync(fixture.Owner);
    }
}
