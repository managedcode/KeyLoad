using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBm25EligibilityTrial
{
    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var second = Mutation(flow, new PutDocument("orders", "second", "{\"text\":\"needle needle\"}"));
        var noise = Mutation(flow, new PutDocument("orders", "noise", "{\"text\":\"quiet\"}"));
        var original = flow.Capture(null);
        var receipt = flow.Publish(original);
        await Assert.That(receipt.Error).IsNull();
        var entry = NativeSerialization.Serialize(flow.Owner.Entry(original.Id));
        var head = NativeSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((original).Value!.Input.Span).Current;
        var raw = Bm25ColdInvalidationAssertions.HeadBytes(flow.Owner, head);
        await Assert.That(head.Statistics.DocumentCount).IsEqualTo(3);
        await Assert.That(head.Statistics.TotalLength).IsEqualTo(4L);
        await Assert.That(head.Statistics.Terms.Single()).IsEqualTo("needle");
        await Assert.That(head.Statistics.DocumentFrequencies.Single()).IsEqualTo(2);
        await NativeTextBm25EligibilityPages.RequireAsync(flow, true, false, false);
        var patch = Mutation(flow, new PatchDocument("orders", "first",
            [new("/text", PatchKind.Set, "\"needle needle needle\"")], 1));
        await NativeTextBm25EligibilityPages.RequireAsync(flow, false, true, false);
        var deleted = Mutation(flow, new DeleteDocument("orders", "noise", 1));
        await NativeTextBm25EligibilityPages.RequireAsync(flow, false, true, false);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(raw, Bm25ColdInvalidationAssertions.HeadBytes(flow.Owner, head));
        await Bm25ColdInvalidationAssertions.MutationReplayAsync(flow, [second, noise, patch, deleted]);
        await Bm25ColdInvalidationAssertions.ReplayAsync(flow, original, receipt, entry);
        await NativeTextBm25EligibilityPages.RequireAsync(flow, false, true, false);
        await NativeTextBm25EligibilityPolicy.RunAsync(flow, original);
    }

    internal static (Command<DatabaseCommand> Operation, Result<object> Result, byte[] Entry) Mutation(
        Bm25OriginalPhaseRecoveryFlow flow, Mutation mutation)
    {
        var owner = flow.Owner;
        var id = Guid.NewGuid();
        var operation = owner.Database.CreateCommand(OperationKind.Batch, id, "root",
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(
                new CommandRequest(id, owner.Source.Partition, [mutation])));
        var result = owner.Submit(operation);
        if (Errors.Code(result.Problem) is not null)
        {
            throw new InvalidOperationException("The genuine canonical mutation was not admitted.");
        }
        return (operation, result, NativeSerialization.Serialize(owner.Entry(id)));
    }
}
