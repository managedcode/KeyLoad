namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphLabelUseFixture
{
    internal const string Collection = "label_use_vertices";
    internal const string Graph = "label_use_graph";
    internal const string Reader = "label-use-reader";
    internal const string ReadGrant = "label.read";
    internal const string UseGrant = "label.use";
    internal const string AttributeJson = "{\"label\":\"attribute-value\",\"weight\":1}";
    internal const string DeniedDetail = "The query requires a protected field-use grant.";
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, Collection, id);
    internal static EdgeRecord[] Edges(PartitionRef partition)
        => [new("ab", Vertex(partition, "a"), Vertex(partition, "b"), "route", AttributeJson, 1L),
            new("bc", Vertex(partition, "b"), Vertex(partition, "c"), "skip", AttributeJson, 1L)];
    internal static async Task<(CommandRequest Command, CommitReceipt Receipt, byte[] Outcome)> SeedAsync(TestDatabase db)
    {
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph, fields: [new("/label", "private", ReadGrant, UseGrant)]);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(Collection, "a", "{}"), new PutDocument(Collection, "b", "{}"), new PutDocument(Collection, "c", "{}"),
                .. Edges(db.Partition).Select(edge => new UpsertEdge(Graph, edge.Id, edge.From, edge.To, edge.Label, edge.AttributesJson))]);
        var before = db.Store.Position;
        var nextCanonical = checked(db.Database.LastApplied + 1L);
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        var expected = new CommitReceipt(command.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, nextCanonical), [new("putDocument", Collection, "a", 1L), new("putDocument", Collection, "b", 1L),
                new("putDocument", Collection, "c", 1L), new("upsertEdge", Graph, "ab", 1L),
                new("upsertEdge", Graph, "bc", 1L)], DurabilityProfile.ProcessDurable, 1);
        await GraphLabelUseOperations.EqualAsync(expected, receipt);
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
        await Assert.That(db.Store.Position).IsEqualTo(before + 1L);
        var outcome = db.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(db.Partition, "root", command.CommandId)))
            ?? throw new InvalidOperationException("The original acknowledged graph outcome must exist.");
        return (command, receipt, outcome);
    }
    internal static PrincipalRecord Policy(bool allow, long epoch)
        => new(Reader, "tenant", [new("database", Collection, Capability.DocumentsRead),
            new("database", Graph, Capability.GraphRead)], allow ? [ReadGrant, UseGrant] : [ReadGrant])
        {
            PolicyEpoch = epoch
        };
    internal static async Task PersistAsync(TestDatabase db, bool allow, long epoch)
    {
        var expected = Policy(allow, epoch);
        await GraphLabelUseOperations.EqualAsync(expected,
            db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>());
    }
    internal static global::KeyLoad.GraphTraversal Expected(TestDatabase source, string[]? labels)
    {
        var partition = source.Partition;
        global::KeyLoad.GraphTraversal expected = labels is null || labels.Length == 0
            ? new([Vertex(partition, "a"), Vertex(partition, "b"), Vertex(partition, "c")], [.. Edges(partition)])
            : labels.Contains("route", StringComparer.Ordinal)
                ? new([Vertex(partition, "a"), Vertex(partition, "b")], [Edges(partition)[0]])
                : new([Vertex(partition, "a")], []);
        return new([.. expected.Vertices.Select(source.ExpectedReference)],
            [.. expected.Edges.Select(edge => edge with
            {
                From = source.ExpectedReference(edge.From),
                To = source.ExpectedReference(edge.To)
            })]);
    }
    internal static global::KeyLoad.GraphTraversal Traverse(DatabaseEngine db, PartitionRef partition, string[]? labels,
        CancellationToken token, string start = "a")
        => db.Traverse(Reader, partition, Graph, Vertex(partition, start), 2, 3, 2, labels, token);
}
