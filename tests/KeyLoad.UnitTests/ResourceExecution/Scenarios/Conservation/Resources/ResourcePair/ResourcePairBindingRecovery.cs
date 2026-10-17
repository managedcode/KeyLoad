using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingRecovery
{
    internal static async Task<(ReplicatedOperation Original, OperationResult Result)> ApplyAsync(ResourcePairBindingState state,
        ReplicatedOperation issued, string resource, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            return await ApplyCoreAsync(state, issued, resource, failures, token);
        }
        catch (Exception failure)
        {
            failure.Data[SubmissionFailuresKey] = failures.ToArray();
            throw;
        }
    }

    private static async Task<(ReplicatedOperation Original, OperationResult Result)> ApplyCoreAsync(ResourcePairBindingState state,
        ReplicatedOperation issued, string resource, List<Exception> failures, CancellationToken token)
    {
        var source = state.Source;
        var damagedRow = ResourcePairBindingFaults.Install(state, resource);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        var journal = source.ReadJournalCut();
        var first = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssuedEmbedded(issued, token));
        failures.Add(first);
        await Assert.That(first.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        var retained = source.ReadRetainedEntry(issued.Id);
        var original = retained.Operation ?? throw new InvalidOperationException(Invalid);
        await Assert.That(original.PayloadJson).IsEqualTo(issued.PayloadJson);
        await Assert.That(original.Id).IsEqualTo(issued.Id);
        await Assert.That(original.PrincipalId).IsEqualTo(issued.PrincipalId);
        await Assert.That(original.Kind).IsEqualTo(issued.Kind);
        await Assert.That(retained.Index).IsEqualTo(journal.LastIndex + One);
        await Assert.That(source.ReadJournalCut().CommittedIndex).IsEqualTo(retained.Index);
        await RefusedAsync(source, original, image, position);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => source.SubmitIssued(original));
        failures.Add(repeated);
        await Assert.That(repeated.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await RefusedAsync(source, original, image, position);
        var applied = source.RecoverFaultOwnedRow(original, () => ResourcePairBindingFaults.Repair(source.Store, damagedRow),
            failures, token, ErrorCode.Corruption, ErrorCode.Corruption);
        await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures, ErrorCode.Corruption);
        await ResourcePairBindingAssertions.EqualAsync(retained, applied);
        await Assert.That(applied.Operation!.PayloadJson).IsEqualTo(original.PayloadJson);
        await Assert.That(applied.Operation.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        await Assert.That(source.Database.LastApplied).IsEqualTo(retained.Index);
        await Assert.That(source.ReadJournalCut().LastIndex).IsEqualTo(retained.Index);
        var result = source.SubmitIssued(original);
        ResourcePairBindingAssertions.Remember(state, original, result);
        await ResourcePairBindingAssertions.PairsAsync(state, source.Store);
        return (original, result);
    }

    private static async Task RefusedAsync(TestDatabase source, ReplicatedOperation original, string[] image, long position)
    {
        var result = source.Database.ResolveOutcome(original);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
            source.Partition, original.PrincipalId, original.Id)))).IsNull();
        await ResourcePairBindingAssertions.UnchangedAsync(source.Store, image, position);
    }
}
