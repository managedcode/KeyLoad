using KeyLoad.Core.Search;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Query.Search;
using ManagedCode.Communication.Commands;
using static KeyLoad.FunctionalTests.ResourceExecution.ResourcePairBindingState;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class ResourcePairBindingBm25
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        _ = source.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(source.Partition.TenantId,
            source.Partition.DatabaseId, new(Corpus, ResourceKind.Collection, source.Partition.TransactionDomainId))).Get<ResourceDefinition>();
        state.Pairs.Add(Corpus, (ResourceIncarnationAssertions.Bytes(source.Store, KeySpace.Resource(source.Partition.TenantId,
            source.Partition.DatabaseId, Corpus)), ResourceIncarnationAssertions.Bytes(source.Store,
            ResourcePairBindingFlow.LifetimeKey(source.Partition, Corpus))));
        foreach (var id in new[] { CorpusSubject, CorpusPublisher })
        {
            _ = source.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id, source.Partition.TenantId,
                [new(source.Partition.DatabaseId, Corpus, Capability.Query | Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        }
        var request = new CommandRequest(Guid.NewGuid(), source.Partition, [new PutDocument(Corpus, CorpusDocument, CorpusJson)]);
        var seedIndex = source.Database.LastApplied + One;
        var seeded = source.SubmitIssuedEmbedded(ResourcePairBindingBatch.Issue(source.Database, request), token);
        state.Remember(source.ReadStoredOutcome(request.CommandId).PreparedCommand, seeded);
        await ResourceIncarnationAssertions.EqualAsync(ResourcePairBindingAssertions.Receipt(source.Store, source.Partition,
            request.CommandId, seedIndex, [new(PutKind, Corpus, CorpusDocument, FirstRevision)]), seeded.Get<CommitReceipt>());
        var damaged = ResourcePairBindingFlow.Install(state, Corpus);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        var cut = source.ReadCanonicalCut();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => Capture(state, CorpusPublisher, token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(CorpusDeniedDetail);
        await ResourceIncarnationAssertions.UnchangedAsync(source.Store, image, position);
        await Assert.That(source.ReadCanonicalCut()).IsEqualTo(cut);
        ResourcePairBindingFlow.Repair(source.Store, damaged);
        var original = Capture(state, Root, token);
        var phase = ((Bm25CorpusPublicationPhaseCommand)original.Value!.Input);
        await ResourceIncarnationAssertions.EqualAsync(new DistributedTextStatistics([CorpusTerm], One, One, [One]), phase.Current.Statistics);
        var expectedIndex = source.Store.Position + One;
        var canonical = source.Database.LastApplied;
        var result = source.SubmitIssuedEmbedded(original, token);
        await ResourceIncarnationAssertions.EqualAsync(ResourcePairBindingAssertions.Receipt(source.Store, source.Partition,
            original.CommandId, expectedIndex, []), result.Get<CommitReceipt>());
        await Assert.That(source.Database.LastApplied).IsEqualTo(canonical);
        state.Remember(source.ReadStoredOutcome(original.CommandId).PreparedCommand, result);
        image = QueueWholeFlowStorage.Bytes(source.Store);
        position = source.Store.Position;
        cut = source.ReadCanonicalCut();
        await ResourceIncarnationAssertions.ResultAsync(result, source.SubmitIssued(original));
        await ResourceIncarnationAssertions.UnchangedAsync(source.Store, image, position);
        await Assert.That(source.ReadCanonicalCut()).IsEqualTo(cut);
        await HealthyAsync(state, source.Database, token);
    }

    private static Command<DatabaseCommand> Capture(ResourcePairBindingState state, string publisher, CancellationToken token)
        => Bm25CorpusCapture.Create(state.Source.Database, publisher, CorpusSubject,
            new(state.Source.Partition, Corpus, CorpusField, CorpusTerm), Guid.NewGuid(), null,
            state.Source.CreateReadWork(token), UnitExecutionOptions.QueryExecution());

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database, CancellationToken token, bool expectedCurrent = true)
    {
        var current = database.WithQueryView(CorpusSubject, state.Source.Partition, Corpus,
            (view, principal, resource) => (database.ReadBm25CorpusCurrent(view, principal, resource, state.Source.Partition,
                Corpus, CorpusField, TextProjectionProtocol.TokenizerVersion, [CorpusTerm], new ReadExecutionBudget(database.EvaluationClock, token))).Value);
        if (expectedCurrent)
        {
            await ResourceIncarnationAssertions.EqualAsync(new DistributedTextStatistics([CorpusTerm], One, One, [One]), current!);
        }
        else
        {
            await Assert.That(current).IsNull();
        }
        await ResourceIncarnationAssertions.EqualAsync(new DocumentResult(state.Source.ExpectedReference(new(state.Source.Partition, Corpus, CorpusDocument)),
            FirstRevision, CorpusJson, false, []), database.GetDocument(Root, new(state.Source.Partition, Corpus, CorpusDocument))!);
    }
}
