using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingBm25
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        _ = source.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(source.Partition.TenantId,
            source.Partition.DatabaseId, new(Corpus, ResourceKind.Collection, source.Partition.TransactionDomainId))).Get<ResourceDefinition>();
        state.Pairs.Add(Corpus, (ResourceIncarnationAssertions.Bytes(source.Store, KeySpace.Resource(source.Partition.TenantId,
            source.Partition.DatabaseId, Corpus)), ResourceIncarnationAssertions.Bytes(source.Store,
            ResourcePairBindingFaults.LifetimeKey(source.Partition, Corpus))));
        foreach (var id in new[] { CorpusSubject, CorpusPublisher })
        {
            _ = source.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id, source.Partition.TenantId,
                [new(source.Partition.DatabaseId, Corpus, Capability.Query | Capability.DocumentsRead)], []))).Get<PrincipalRecord>();
        }
        var request = new CommandRequest(Guid.NewGuid(), source.Partition, [new PutDocument(Corpus, CorpusDocument, CorpusJson)]);
        var seedIndex = source.ReadJournalCut().LastIndex + One;
        var seeded = source.SubmitIssuedEmbedded(ResourcePairBindingBatch.Issue(source.Database, request), token);
        ResourcePairBindingAssertions.Remember(state, source.ReadRetainedEntry(request.CommandId).Operation!, seeded);
        await ResourcePairBindingAssertions.EqualAsync(ResourcePairBindingAssertions.Receipt(source.Store, source.Partition,
            request.CommandId, seedIndex, [new(PutKind, Corpus, CorpusDocument, FirstRevision)]), seeded.Get<CommitReceipt>());
        var damaged = ResourcePairBindingFaults.Install(state, Corpus);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        var journal = source.ReadJournalCut();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => Capture(state, CorpusPublisher, token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(CorpusDeniedDetail);
        await ResourcePairBindingAssertions.UnchangedAsync(source.Store, image, position);
        await Assert.That(source.ReadJournalCut()).IsEqualTo(journal);
        ResourcePairBindingFaults.Repair(source.Store, damaged);
        var original = Capture(state, Root, token);
        var phase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(original);
        await ResourcePairBindingAssertions.EqualAsync(new DistributedTextStatisticsV1([CorpusTerm], One, One, [One]), phase.Current.Statistics);
        var expectedIndex = source.ReadJournalCut().LastIndex + One;
        var result = source.SubmitIssuedEmbedded(original, token);
        await ResourcePairBindingAssertions.EqualAsync(ResourcePairBindingAssertions.Receipt(source.Store, source.Partition,
            original.Id, expectedIndex, []), result.Get<CommitReceipt>());
        ResourcePairBindingAssertions.Remember(state, source.ReadRetainedEntry(original.Id).Operation!, result);
        image = QueueWholeFlowStorage.Bytes(source.Store);
        position = source.Store.Position;
        journal = source.ReadJournalCut();
        await ResourceIncarnationAssertions.ResultAsync(result, source.SubmitIssued(original));
        await ResourcePairBindingAssertions.UnchangedAsync(source.Store, image, position);
        await Assert.That(source.ReadJournalCut()).IsEqualTo(journal);
        await HealthyAsync(state, source.Database, token);
    }

    private static ReplicatedOperation Capture(ResourcePairBindingState state, string publisher, CancellationToken token)
        => Bm25CorpusCapture.Create(state.Source.Database, publisher, CorpusSubject,
            new(state.Source.Partition, Corpus, CorpusField, CorpusTerm), Guid.NewGuid(), null,
            state.Source.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            state.Source.CreateReadWork(token), UnitExecutionOptions.QueryExecution());

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database, CancellationToken token, bool expectedCurrent = true)
    {
        var current = database.WithQueryView(CorpusSubject, state.Source.Partition, Corpus,
            (view, principal, resource) => database.ReadBm25CorpusCurrent(view, principal, resource, state.Source.Partition,
                Corpus, CorpusField, TextProjectionProtocol.TokenizerVersion, [CorpusTerm], new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token)));
        if (expectedCurrent)
        {
            await ResourcePairBindingAssertions.EqualAsync(new DistributedTextStatisticsV1([CorpusTerm], One, One, [One]), current!);
        }
        else
        {
            await Assert.That(current).IsNull();
        }
        await ResourcePairBindingAssertions.EqualAsync(new DocumentResult(new(state.Source.Partition, Corpus, CorpusDocument),
            FirstRevision, CorpusJson, false, []), database.GetDocument(Root, new(state.Source.Partition, Corpus, CorpusDocument))!);
    }
}
