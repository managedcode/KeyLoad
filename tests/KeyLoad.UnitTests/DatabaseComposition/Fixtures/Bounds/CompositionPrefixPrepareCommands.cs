using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionPrefixPrepareCommands
{
    internal static CompositionPrefixPrepareCommand Issue(CompositionPrefixPrepareOwner owner,
        ImmutableArray<Mutation> mutations, string principal = D.Root, DateTimeOffset? evaluatedAt = null)
    {
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition, mutations);
        var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, owner.Token);
        var original = owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, principal,
            evaluatedAt ?? owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
        var decoded = OrleansSerialization.Deserialize<CommandRequest>((original).Value!.Input.Span);
        return new(decoded, original, OrleansSerialization.Serialize(original), null, null, null, work);
    }

    internal static async Task<CompositionPrefixPrepareCommand> CommitAsync(CompositionPrefixPrepareOwner owner,
        ImmutableArray<Mutation> mutations, ImmutableArray<MutationReceipt> receipts, int? bindings = null,
        string principal = D.Root)
    {
        var issued = Issue(owner, mutations, principal);
        if (bindings is { } count)
        {
            var admission = ResourceEffectAdmissionValidation.Read(issued.Original, owner.Database.OperationLimitsOptions.Value);
            await Assert.That(admission.Error).IsNull();
            await Assert.That(admission.Bindings.Length).IsEqualTo(count);
            await Assert.That(admission.Bindings.ToHashSet().SetEquals(new[] { D.From, D.To, D.Queue, D.Graph }
                .Select(name => owner.Lifetimes[name]))).IsTrue();
        }
        var expected = new CommitReceipt(issued.Original.CommandId, owner.NextToken(), receipts, DurabilityProfile.ProcessDurable, 1);
        issued = issued with
        {
            Receipt = expected
        };
        var charged = issued.Work.ReadBytes;
        await Assert.That(charged > D.Absent).IsTrue();
        await CompositionPrefixPrepareAssertions.ResultAsync(issued, owner.Submit(issued.Original));
        await Assert.That(issued.Work.ReadBytes).IsEqualTo(charged);
        owner.Commands.Add(issued);
        return issued;
    }

    internal static QueueToGraph Forward(string graph = D.Graph, string prefix = D.Forward, int maximum = D.FirstRevision, bool typed = true)
        => new(graph, D.Queue, prefix, maximum, typed ? D.From : null, typed ? D.To : null);

    internal static MutationReceipt Edge(CompositionPrefixPrepareOwner owner, string graph, string id)
        => new(D.Upsert, graph, id, D.FirstRevision)
        {
            CompositionReferences = [CompositionPrefixPrepareSeed.Bound(owner, D.From, D.First),
                CompositionPrefixPrepareSeed.Bound(owner, D.To, D.Second)]
        };
}
