using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.Messaging.RecurringSagaSchemaProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RecurringSagaSchemaFlow
{
    internal static Task RunAsync(CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(ExecuteAsync, token);

    private static async Task ExecuteAsync(SafeProjectorOperationDatabase owner)
    {
        var state = await SeedAsync(owner);
        await RecurringSagaSchemaProjection.AllAsync(owner, state, bodySchema: false, headerSchema: false);
        state = await RepairAsync(owner, state, [BodySchema]);
        await RecurringSagaSchemaProjection.AllAsync(owner, state, bodySchema: true, headerSchema: false);
        state = await RepairAsync(owner, state, [HeaderSchema]);
        await RecurringSagaSchemaProjection.AllAsync(owner, state, bodySchema: false, headerSchema: true);
        state = await RepairAsync(owner, state, [BodySchema, HeaderSchema]);
        await RecurringSagaSchemaProjection.AllAsync(owner, state, bodySchema: true, headerSchema: true);
        state = await RepairAsync(owner, state, []);
        await RecurringSagaSchemaProjection.AllAsync(owner, state, bodySchema: false, headerSchema: false);
        await RecurringSagaSchemaProjection.CancelledAsync(owner, state);
        await RecurringSagaSchemaCold.RunAsync(owner, state);
    }

    private static async Task<RecurringSagaSchemaState> SeedAsync(SafeProjectorOperationDatabase owner)
    {
        var partition = owner.Source.Partition;
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            FieldPolicies = [new(SecretPath, "restricted", SchemaReadGrant: BodySchema)],
            HeaderPolicies = [new(SecretPath, "restricted", SchemaReadGrant: HeaderSchema)]
        };
        var configured = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource));
        await SafeProjectorOperationSeed.EqualAsync(configured.Result.Get<ResourceDefinition>(), resource);
        var scheduler = new PrincipalRecord(Scheduler, partition.TenantId,
            [new(partition.DatabaseId, Queue, Capability.SchedulerManage | Capability.QueuePublish)], [WriteGrant]);
        var creator = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(scheduler));
        await SafeProjectorOperationSeed.EqualAsync(creator.Result.Get<PrincipalRecord>(), scheduler);
        var reader = Principal(owner, InitialEpoch, []);
        var granted = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(reader));
        var denied = new PrincipalRecord(Denied, partition.TenantId, [], []);
        var refused = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(denied));
        await SafeProjectorOperationSeed.EqualAsync(granted.Result.Get<PrincipalRecord>(), reader);
        await SafeProjectorOperationSeed.EqualAsync(refused.Result.Get<PrincipalRecord>(), denied);
        var definition = new RecurringScheduleDefinition(new(partition, Queue), ScheduleId,
            owner.Database.EvaluationClock.GetUtcNow() + Interval, Interval, Utc,
            RecurringMisfirePolicy.CatchUp, Body, Headers);
        var schedule = new ConfigureRecurringSchedule(definition, Zero);
        var saga = new CompareExchangeSaga(definition.Lane, SagaId, Zero, SagaPhase.Waiting, Body);
        var id = Guid.NewGuid();
        var seed = SafeProjectorOperationSeed.Issue(owner, OperationKind.Batch, id,
            new CommandRequest(id, partition, [schedule, saga]), principal: Scheduler, creatorEpoch: InitialEpoch);
        var expected = new CommitReceipt(id, seed.ExpectedToken,
            [new(ScheduleKind, Queue, ScheduleId.ToString(Format), One),
                new(SagaKind, Queue, SagaId.ToString(Format), One)], DurabilityProfile.ProcessDurable);
        await SafeProjectorOperationSeed.EqualAsync(seed.Result.Get<CommitReceipt>(), expected);
        await RecurringSagaSchemaCanonical.PrivateReferencesAsync(seed.Result.Get<CommitReceipt>());
        var state = new RecurringSagaSchemaState(resource, scheduler, reader, denied, definition, schedule, saga,
            seed, [configured, creator, granted, refused, seed]);
        await RecurringSagaSchemaCanonical.CurrentAsync(owner, state);
        return state;
    }

    private static async Task<RecurringSagaSchemaState> RepairAsync(SafeProjectorOperationDatabase owner,
        RecurringSagaSchemaState state, ImmutableArray<string> schemaGrants)
    {
        var principal = Principal(owner, checked(state.Reader.PolicyEpoch + One), schemaGrants);
        var command = SafeProjectorOperationSeed.Issue(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(principal));
        await SafeProjectorOperationSeed.EqualAsync(command.Result.Get<PrincipalRecord>(), principal);
        var repaired = state with
        {
            Reader = principal,
            History = state.History.Add(command)
        };
        await RecurringSagaSchemaCanonical.CurrentAsync(owner, repaired);
        return repaired;
    }

    private static PrincipalRecord Principal(SafeProjectorOperationDatabase owner, long epoch,
        ImmutableArray<string> schemaGrants) => new(Reader, owner.Source.Partition.TenantId,
            [new(owner.Source.Partition.DatabaseId, Queue, Capability.QueueInspect)], schemaGrants)
        {
            PolicyEpoch = epoch
        };
}
