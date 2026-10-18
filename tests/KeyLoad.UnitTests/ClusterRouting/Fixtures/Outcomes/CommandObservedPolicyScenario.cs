using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CommandObservedPolicyScenario(bool header)
{
    internal ConnectionOperationObservation Observation { get; } = new();
    internal CommandObservedPolicyRuntime Fixture { get; private set; } = null!;
    internal ResourceDefinition Resource { get; private set; } = null!;
    internal PrincipalRecord Worker { get; private set; } = null!;
    private ResourceLifetimeV1 lifetime = null!;
    internal QueueLaneRef Lane => new(Fixture.Database.Partition, P.Queue);

    internal async Task InitializeAsync(CancellationToken token)
    {
        Fixture = await CommandObservedPolicyRuntime.CreateAsync(Observation, token);
        var partition = Fixture.Database.Partition;
        var policy = new SensitiveFieldPolicy(P.Path, P.Classification, P.BeforeGrant);
        Resource = CommandObservedPolicyAssertions.Value<ResourceDefinition>(await Fixture.Database.SubmitAsync(
            OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(P.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
                {
                    FieldPolicies = header ? [] : [policy],
                    HeaderPolicies = header ? [policy] : []
                }), token));
        Worker = CommandObservedPolicyAssertions.Value<PrincipalRecord>(await Fixture.Database.SubmitAsync(
            OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(P.Worker, partition.TenantId,
                [new(partition.DatabaseId, P.Queue, Capability.QueueConsume | Capability.QueueInspect)],
                [P.BeforeGrant])), token));
        lifetime = Lifetime();
        await SeedAsync(token);
    }

    private async Task SeedAsync(CancellationToken token)
    {
        await Fixture.Database.Coordinator.ReadBarrierAsync(token);
        var partition = Fixture.Database.Partition;
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition,
            [new EnqueueMessage(P.Queue, P.First, P.Payload, P.Headers),
                new EnqueueMessage(P.Queue, P.Second, P.Payload, P.Headers)]);
        var index = checked(Fixture.Database.ReadCanonicalCut().AppliedPosition + P.Step);
        var actual = CommandObservedPolicyAssertions.Value<CommitReceipt>(await Fixture.Database.SubmitAsync(
            OperationKind.Batch, command, token, id: id));
        var expected = new CommitReceipt(id, new(Fixture.Database.Store.Identity.Incarnation, partition.AtomicPartitionId, index), [new(P.EnqueueKind, P.Queue, P.First, P.Initial), new(P.EnqueueKind, P.Queue, P.Second, P.Initial)], DurabilityProfile.ProcessDurable, 1);
        await CommandObservedPolicyAssertions.EqualAsync(expected, actual);
    }

    internal async Task PolicyAsync(bool repaired, CancellationToken token)
    {
        var partition = Fixture.Database.Partition;
        var policy = new SensitiveFieldPolicy(P.Path, P.Classification, repaired ? P.BeforeGrant : P.AfterGrant);
        var definition = Resource with
        {
            SchemaVersion = checked(Resource.SchemaVersion + P.Step),
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        Resource = CommandObservedPolicyAssertions.Value<ResourceDefinition>(await Fixture.Database.SubmitAsync(
            OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition)
            {
                ExpectedSchemaVersion = Resource.SchemaVersion
            }, token));
        await Fixture.Database.Coordinator.ReadBarrierAsync(token);
        await CommandObservedPolicyAssertions.EqualAsync(definition, Resource);
        var current = Fixture.Database.Store.Read(view => Fixture.Database.Database.Principal(view,
            P.Worker, Fixture.Source.Clock.GetUtcNow()));
        await CommandObservedPolicyAssertions.EqualAsync(Worker, current);
        await CommandObservedPolicyAssertions.EqualAsync(lifetime, Lifetime());
    }

    private ResourceLifetimeV1 Lifetime()
    {
        var partition = Fixture.Database.Partition;
        return Fixture.Database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            partition.TenantId, partition.DatabaseId, P.Queue, Resource))
            ?? throw new InvalidOperationException(P.Missing);
    }

    internal ReceiveRequest Receive()
        => new(Guid.NewGuid(), Lane, MaxMessages: P.One, LeaseSeconds: Resource.QueuePolicy.MaxLeaseSeconds);
}
