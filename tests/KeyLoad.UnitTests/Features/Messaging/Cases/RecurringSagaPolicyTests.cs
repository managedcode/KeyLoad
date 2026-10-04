using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class RecurringSagaPolicyTests
{
    private const string Scheduler = "scheduler";
    private const string Caller = "caller";
    private const string Inspector = "inspector";
    private const string NoInspector = "no-inspector";
    private const string NoPublish = "no-publish";
    private const string NoWrite = "no-write";
    private const string AdminWithoutScheduler = "admin-without-scheduler";
    private const string Payload = "{\"secret\":\"payload\"}";
    private const string UpdatedState = "{\"secret\":\"updated\"}";
    private const string Headers = "{\"secret\":\"header\"}";
    private const string RawUse = RecurringSagaDatabase.RawUseGrant;
    private const string FieldWrite = RecurringSagaDatabase.FieldWriteGrant;

    [Test]
    public async Task SchedulerCapabilitiesRemainRequiredEvenForClusterAdministrator()
    {
        var protectedPolicy = new[]
        {
            new SensitiveFieldPolicy(RecurringSagaDatabase.SecretPath, "restricted")
        };
        using var fixture = new RecurringSagaDatabase(fields: protectedPolicy, headers: protectedPolicy);
        fixture.AddPrincipal(Principal(NoPublish, Capability.SchedulerManage, [FieldWrite]));
        fixture.AddPrincipal(Principal(NoWrite, Capability.SchedulerManage | Capability.QueuePublish, []));
        fixture.AddPrincipal(new(AdminWithoutScheduler, RecurringSagaDatabase.TenantId, [], [])
            { ClusterAdministrator = true });
        var id = Guid.NewGuid();
        var definition = Definition(fixture, id);

        await AssertDenied(fixture, NoPublish, new ConfigureRecurringSchedule(definition, 0));
        await AssertDenied(fixture, NoWrite, new ConfigureRecurringSchedule(definition, 0));
        await AssertDenied(fixture, AdminWithoutScheduler, new ConfigureRecurringSchedule(definition, 0));
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, id)).IsNull();
    }

    [Test]
    public async Task EmissionRechecksPersistedCreatorUseAndHealthyRetry()
    {
        var protectedPolicy = new[]
        {
            new SensitiveFieldPolicy(RecurringSagaDatabase.SecretPath, "restricted")
        };
        using var fixture = new RecurringSagaDatabase(fields: protectedPolicy, headers: protectedPolicy);
        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish,
            [FieldWrite, RawUse]));
        fixture.AddPrincipal(Principal(Caller, Capability.SchedulerManage | Capability.QueuePublish,
            [FieldWrite, RawUse]));
        var scheduleId = Guid.NewGuid();
        var schedule = Definition(fixture, scheduleId);
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(schedule, 0));
        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish, [FieldWrite]));

        var denied = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition,
                [new EmitRecurringOccurrences(fixture.Queue, scheduleId, 1)]), Caller,
            time: RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!.NextOrdinal).IsEqualTo(0);

        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish,
            [FieldWrite, RawUse]), RecurringSagaDatabase.Epoch.AddSeconds(1));
        fixture.AddPrincipal(Principal(Caller, Capability.SchedulerManage | Capability.QueuePublish,
            [FieldWrite, RawUse]), RecurringSagaDatabase.Epoch.AddSeconds(1));
        var emitted = fixture.CommitAs(Caller, fixture.Partition, RecurringSagaDatabase.Epoch.AddSeconds(1),
            new EmitRecurringOccurrences(fixture.Queue, scheduleId, 1));
        await Assert.That(emitted.Mutations[0].Revision).IsEqualTo(1);
    }

    [Test]
    public async Task InspectionRequiresPersistedQueueGrantAndProjectsCurrentFieldPolicies()
    {
        var protectedPolicy = new[]
        {
            new SensitiveFieldPolicy(RecurringSagaDatabase.SecretPath, "restricted")
        };
        using var fixture = new RecurringSagaDatabase(fields: protectedPolicy, headers: protectedPolicy);
        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish,
            [FieldWrite, RawUse]));
        fixture.AddPrincipal(Principal(Inspector, Capability.QueueInspect, []));
        fixture.AddPrincipal(Principal(NoInspector, Capability.None, []));
        var scheduleId = Guid.NewGuid();
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(Definition(fixture, scheduleId), 0));
        var sagaId = Guid.NewGuid();
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new CompareExchangeSaga(fixture.Queue, sagaId, 0, SagaPhase.Waiting, Payload));

        var visible = fixture.Database.InspectRecurringSchedule(Inspector, fixture.Queue, scheduleId)!;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Database.InspectRecurringSchedule(NoInspector, fixture.Queue, scheduleId));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(visible.Definition.PayloadJson).IsEqualTo("{}");
        await Assert.That(visible.Definition.HeadersJson).IsEqualTo("{}");
        await Assert.That(visible.Redacted).IsTrue();
        await Assert.That(visible.RedactedFields).IsEquivalentTo(
            ["payload:secret", "headers:secret"], CollectionOrdering.Matching);

        var sagaView = fixture.Database.InspectSaga(Inspector, fixture.Queue, sagaId)!;
        await Assert.That(sagaView.StateJson).IsEqualTo("{}");
        await Assert.That(sagaView.Redacted).IsTrue();
        await Assert.That(sagaView.RedactedFields).IsEquivalentTo(["state:secret"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task EmissionRechecksPersistedCreatorSchedulerCapability()
    {
        using var fixture = new RecurringSagaDatabase();
        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish, []));
        fixture.AddPrincipal(Principal(Caller, Capability.SchedulerManage | Capability.QueuePublish, []));
        var id = Guid.NewGuid();
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(Definition(fixture, id), 0));
        fixture.AddPrincipal(Principal(Scheduler, Capability.QueuePublish, []));
        var rejected = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition,
                [new EmitRecurringOccurrences(fixture.Queue, id, 1)]), Caller,
            time: RecurringSagaDatabase.Epoch.AddSeconds(1));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, id)!.NextOrdinal).IsEqualTo(0);

        fixture.AddPrincipal(Principal(Scheduler, Capability.SchedulerManage | Capability.QueuePublish, []),
            RecurringSagaDatabase.Epoch.AddSeconds(1));
        var allowed = fixture.CommitAs(Caller, fixture.Partition, RecurringSagaDatabase.Epoch.AddSeconds(1),
            new EmitRecurringOccurrences(fixture.Queue, id, 1));
        await Assert.That(allowed.Mutations[0].Revision).IsEqualTo(1);
    }

    [Test]
    public async Task ReplacementAndSagaCasRecheckRetainedCreatorFieldWriteAuthority()
    {
        var protectedPolicy = new[]
        {
            new SensitiveFieldPolicy(RecurringSagaDatabase.SecretPath, "restricted")
        };
        using var fixture = new RecurringSagaDatabase(fields: protectedPolicy, headers: protectedPolicy);
        var capabilities = Capability.SchedulerManage | Capability.QueuePublish;
        fixture.AddPrincipal(Principal(Scheduler, capabilities, [FieldWrite]));
        fixture.AddPrincipal(Principal(Caller, capabilities, [FieldWrite]));
        var scheduleId = Guid.NewGuid();
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(Definition(fixture, scheduleId), 0));
        fixture.AddPrincipal(Principal(Scheduler, capabilities, []));
        var scheduleDenied = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition,
                [new ConfigureRecurringSchedule(Definition(fixture, scheduleId), 1)]), Caller);
        await Assert.That(scheduleDenied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(fixture.Database.InspectRecurringSchedule(RecurringSagaDatabase.RootPrincipal,
            fixture.Queue, scheduleId)!.Revision).IsEqualTo(1);

        fixture.AddPrincipal(Principal(Scheduler, capabilities, [FieldWrite]));
        _ = fixture.CommitAs(Caller, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(Definition(fixture, scheduleId), 1));
        var sagaId = Guid.NewGuid();
        _ = fixture.CommitAs(Scheduler, fixture.Partition, RecurringSagaDatabase.Epoch,
            new CompareExchangeSaga(fixture.Queue, sagaId, 0, SagaPhase.Waiting, Payload));
        fixture.AddPrincipal(Principal(Scheduler, capabilities, []));
        var sagaDenied = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition,
                [new CompareExchangeSaga(fixture.Queue, sagaId, 1, SagaPhase.Waiting, UpdatedState)]), Caller);
        await Assert.That(sagaDenied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        var retained = fixture.Database.InspectSaga(RecurringSagaDatabase.RootPrincipal, fixture.Queue, sagaId)!;
        await Assert.That(retained.Revision).IsEqualTo(1);
        await Assert.That(retained.StateJson).IsEqualTo(Payload);
    }

    private static PrincipalRecord Principal(string id, Capability capabilities, string[] fieldGrants)
        => new(id, RecurringSagaDatabase.TenantId,
            [new(RecurringSagaDatabase.DatabaseId, RecurringSagaDatabase.QueueName, capabilities)], [.. fieldGrants]);

    private static RecurringScheduleDefinition Definition(RecurringSagaDatabase fixture, Guid id)
        => new(fixture.Queue, id, RecurringSagaDatabase.Epoch.AddSeconds(1), TimeSpan.FromSeconds(1), "UTC",
            RecurringMisfirePolicy.CatchUp, Payload, Headers);

    private static async Task AssertDenied(RecurringSagaDatabase fixture, string principal,
        ConfigureRecurringSchedule mutation)
    {
        var result = fixture.Apply(OperationKind.Batch,
            new CommandRequest(Guid.NewGuid(), fixture.Partition, [mutation]), principal);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
