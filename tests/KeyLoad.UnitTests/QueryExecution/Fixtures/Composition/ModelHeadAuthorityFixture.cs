using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class ModelHeadAuthorityFixture : IDisposable
{
    internal const string Events = "model-head-events";
    internal const string Stream = "source";
    internal const string Queue = "model-head-queue";
    internal const string Reader = "model-head-reader";
    internal const string Denied = "model-head-denied";
    internal const string Secret = "PRIVATE-MODEL-HEAD";
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal CommandRequest Seed { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal byte[] Outcome { get; private set; } = null!;
    internal byte[] HeadKey => KeySpace.Partition("stream-head", Harness.Partition, Events, Stream);
    internal StreamRef Source => new(Harness.Partition, Events, Stream, 1L);
    internal static string Payload(string visible) => "{\"visible\":\"" + visible + "\",\"secret\":\"" + Secret + "\"}";
    internal static string Headers => "{\"trace\":\"source\",\"privateHeader\":\"" + Secret + "\"}";
    internal static string StoredPayload(string visible) => "{\"secret\":\"" + Secret + "\",\"visible\":\"" + visible + "\"}";
    internal static string StoredHeaders => "{\"privateHeader\":\"" + Secret + "\",\"trace\":\"source\"}";
    internal async Task InitializeAsync()
    {
        foreach (var (name, kind) in new[] { (Events, ResourceKind.StreamSet), (Queue, ResourceKind.WorkQueue) })
        {
            var resource = new ResourceDefinition(name, kind, Harness.Partition.TransactionDomainId)
            {
                FieldPolicies = [new("/secret", "private", "private.read", "private.use")],
                HeaderPolicies = [new("/privateHeader", "private", "private.read", "private.use")]
            };
            await ModelHeadAuthorityOperations.EqualAsync(Harness.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(Harness.Partition.TenantId, Harness.Partition.DatabaseId, resource)).Get<ResourceDefinition>(), resource);
        }
        Seed = new(Guid.NewGuid(), Harness.Partition,
            [new AppendEvents(Events, Stream, [new("created", "Created", Payload("first"), Headers),
                new("paid", "Paid", Payload("second"), Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(Queue, "ready", Payload("queue"), Headers, Epoch)]);
        var before = Harness.Store.Position;
        var expectedToken = Harness.NextCommitToken();
        Receipt = Harness.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        await ModelHeadAuthorityOperations.EqualAsync(Receipt, new CommitReceipt(Seed.CommandId, expectedToken, [new("appendEvents", Events, Stream, 2L), new("enqueue", Queue, "ready", 1L)], DurabilityProfile.ProcessDurable, 1));
        foreach (var effect in Receipt.Mutations)
        {
            await Assert.That(effect.CompositionReferences).IsEmpty();
        }
        await Assert.That(Harness.Store.Position).IsEqualTo(before + 1L);
        Outcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", Seed.CommandId)))
            ?? throw new InvalidOperationException("The original model producer outcome must exist.");
        await PolicyAsync(false, 1L);
        var denied = new PrincipalRecord(Denied, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Events, Capability.Query), new(Harness.Partition.DatabaseId, Queue, Capability.Query)], []);
        await ModelHeadAuthorityOperations.EqualAsync(Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(denied)).Get<PrincipalRecord>(), denied);
    }
    internal async Task PolicyAsync(bool read, long epoch)
    {
        var expected = new PrincipalRecord(Reader, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Events, Capability.Query | (read ? Capability.EventsRead | Capability.EventsReplay : Capability.None)),
             new(Harness.Partition.DatabaseId, Queue, Capability.Query | (read ? Capability.QueueInspect : Capability.None))], [])
        {
            PolicyEpoch = epoch
        };
        await ModelHeadAuthorityOperations.EqualAsync(Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>(), expected);
    }
    public void Dispose() => Harness.Dispose();
}
