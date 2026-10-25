using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.UnitTests.BackupRestore;

namespace KeyLoad.UnitTests.Search;

internal static class Bm25PortableFamilyBackupSeed
{
    internal const string Collection = "bm25-portable-documents";
    internal const string Field = "/text";
    internal const string FirstSubject = "bm25-portable-first";
    internal const string SecondSubject = "bm25-portable-second";
    internal const string Root = "root";

    internal static async Task<(Bm25CorpusCurrent[] Heads, byte[][] Bytes)> CreateAsync(EventingArtifactFixture fixture,
        CancellationToken token)
    {
        var source = fixture.Source;
        source.Configure(Collection, ResourceKind.Collection);
        foreach (var subject in new[] { FirstSubject, SecondSubject })
        {
            var principal = new PrincipalRecord(subject, source.Partition.TenantId,
                [new(source.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [])
            {
                RestrictRows = true,
                OwnerId = subject
            };
            var actual = source.Submit(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
            await Assert.That(actual.PolicyEpoch).IsEqualTo(1L);
            await Assert.That(actual.RestrictRows).IsTrue();
            await Assert.That(actual.OwnerId).IsEqualTo(subject);
        }
        var command = new CommandRequest(Guid.NewGuid(), source.Partition,
            [new PutDocument(Collection, "first", """{"text":"needle alpha"}""", Access: new(OwnerId: FirstSubject)),
             new PutDocument(Collection, "second", """{"text":"needle beta gamma"}""", Access: new(OwnerId: SecondSubject))]);
        _ = source.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        var first = await PublishAsync(source, FirstSubject, token);
        var second = await PublishAsync(source, SecondSubject, token);
        await Assert.That(first.Head.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(second.Head.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(first.Head.Statistics.TotalLength).IsEqualTo(2L);
        await Assert.That(second.Head.Statistics.TotalLength).IsEqualTo(3L);
        await Assert.That(first.Head.Statistics.Terms).IsEquivalentTo(new[] { "needle" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(second.Head.Statistics.DocumentFrequencies).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Bm25CorpusKeys.Current(first.Head).AsSpan().SequenceEqual(Bm25CorpusKeys.Current(second.Head))).IsFalse();
        return ([first.Head, second.Head], [first.Bytes, second.Bytes]);
    }

    internal static async Task<(Bm25CorpusCurrent Head, byte[] Bytes)> PublishAsync(TestDatabase source,
        string subject, CancellationToken token, Guid? expectedGeneration = null)
    {
        var operation = Bm25CorpusCapture.Create(source.Database, Root, subject,
            new(source.Partition, Collection, Field, "needle"), Guid.NewGuid(), expectedGeneration,
            source.Database.EvaluationClock.GetUtcNow() + UnitExecutionOptions.OperationLifetime,
            source.CreateReadWork(token), UnitExecutionOptions.QueryExecution());
        var receipt = source.SubmitIssued(operation).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(source.ReadStoredOutcome(operation.CommandId).PreparedCommand.Value!.Kind)
            .IsEqualTo(OperationKind.Bm25CorpusPublicationPhase);
        var bytes = source.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(
            Bm25CorpusProtocol.CurrentFamily, source.Partition, Collection, Field,
            TextProjectionProtocol.TokenizerVersion, subject)))!;
        var current = OrleansSerialization.Deserialize<Bm25CorpusCurrent>(bytes);
        await Assert.That(current.Partition).IsEqualTo(source.Partition);
        await Assert.That(current.PrincipalId).IsEqualTo(subject);
        await Assert.That(current.PolicyEpoch).IsEqualTo(1L);
        await Assert.That(current.Collection).IsEqualTo(Collection);
        await Assert.That(current.Field).IsEqualTo(Field);
        await Assert.That(current.TokenizerVersion).IsEqualTo(TextProjectionProtocol.TokenizerVersion);
        return (current, bytes);
    }
}
