using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Proves autonomous recurrence honors live creator authority and cancellation across Orleans.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RecurringDueClusterTests(ClusterFixture fixture)
{
    private static readonly Guid RevokedScheduleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid HealthyScheduleId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid CancelledScheduleId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    [Test]
    public async Task AcDue002And004AutonomouslyEmitsOnlyAuthorizedUncancelledOccurrences()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await MessagingClusterScenario.CreateAsync(fixture, deadline.Token);
        var revokedCreator = await RecurringSagaClusterSupport.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        var healthyCreator = await RecurringSagaClusterSupport.CreateSchedulerAsync(fixture, scenario, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, healthyCreator.Secret, IntegrationClientOptions.Execution());
        using var revokedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var revokedSdk = new KeyLoadClient(revokedHttp, revokedCreator.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            healthyCreator.Secret, deadline.Token);
        var firstDueAt = TimeProvider.System.GetUtcNow().Add(DueRecurringClusterProtocol.DueLead);

        var revokedPrincipal = await PrepareSchedulesAsync(fixture, sdk, revokedSdk, mcp, scenario,
            revokedCreator, healthyCreator.Secret, firstDueAt, deadline.Token);
        await VerifyHealthyProgressAsync(sdk, mcp, scenario, healthyCreator.Secret, firstDueAt, deadline.Token);
        await RestoreTemplateUseAsync(fixture, revokedPrincipal, deadline.Token);
        await VerifyRestoredProgressAsync(sdk, mcp, scenario, healthyCreator.Secret, firstDueAt, deadline.Token);
    }

    private static async Task<PrincipalRecord> PrepareSchedulesAsync(ClusterFixture fixture, KeyLoadClient sdk,
        KeyLoadClient revokedSdk, McpOfficialClient mcp, MessagingClusterScenario scenario,
        MessagingClusterIdentity revokedCreator, string inspectorCredential, DateTimeOffset firstDueAt,
        CancellationToken cancellationToken)
    {
        await ConfigureAsync(revokedSdk, scenario, RevokedScheduleId, firstDueAt, cancellationToken);
        await ConfigureAsync(mcp, scenario, HealthyScheduleId, firstDueAt, cancellationToken);
        await ConfigureAsync(sdk, scenario, CancelledScheduleId, firstDueAt, cancellationToken);
        await CancelAsync(mcp, scenario, CancelledScheduleId, cancellationToken);
        var revoked = await RevokeTemplateUseAsync(fixture, revokedCreator, cancellationToken);
        await Assert.That(TimeProvider.System.GetUtcNow() < firstDueAt).IsTrue();
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, HealthyScheduleId,
            revision: 1, generation: 1, nextOrdinal: 0, cancelled: false, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, CancelledScheduleId,
            revision: 2, generation: 1, nextOrdinal: 0, cancelled: true, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, RevokedScheduleId,
            revision: 1, generation: 1, nextOrdinal: 0, cancelled: false, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, RevokedScheduleId,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, HealthyScheduleId,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, CancelledScheduleId,
            cancellationToken);
        return revoked;
    }

    private static async Task VerifyHealthyProgressAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, string inspectorCredential, DateTimeOffset firstDueAt,
        CancellationToken cancellationToken)
    {
        await DueRecurringClusterAssertions.WaitForDueTimeAsync(firstDueAt, cancellationToken);
        await DueRecurringClusterAssertions.WaitForOrdinalAsync(sdk, scenario, HealthyScheduleId, 1, cancellationToken);
        await DueRecurringClusterAssertions.AssertOccurrenceAsync(sdk, mcp, scenario, HealthyScheduleId,
            firstDueAt, inspectorCredential, cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, HealthyScheduleId,
            revision: 1, generation: 1, nextOrdinal: 1, cancelled: false, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, RevokedScheduleId,
            revision: 1, generation: 1, nextOrdinal: 0, cancelled: false, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, RevokedScheduleId,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, CancelledScheduleId,
            revision: 2, generation: 1, nextOrdinal: 0, cancelled: true, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, CancelledScheduleId,
            cancellationToken);
    }

    private static async Task VerifyRestoredProgressAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, string inspectorCredential, DateTimeOffset firstDueAt,
        CancellationToken cancellationToken)
    {
        await DueRecurringClusterAssertions.WaitForOrdinalAsync(sdk, scenario, RevokedScheduleId, 1, cancellationToken);
        await DueRecurringClusterAssertions.AssertOccurrenceAsync(sdk, mcp, scenario, RevokedScheduleId,
            firstDueAt, inspectorCredential, cancellationToken);
        await DueRecurringClusterAssertions.AssertScheduleAsync(sdk, mcp, scenario, RevokedScheduleId,
            revision: 1, generation: 1, nextOrdinal: 1, cancelled: false, firstDueAt, inspectorCredential,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoNextOccurrenceAsync(sdk, mcp, scenario, RevokedScheduleId,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoNextOccurrenceAsync(sdk, mcp, scenario, HealthyScheduleId,
            cancellationToken);
        await DueRecurringClusterAssertions.AssertNoOccurrenceAsync(sdk, mcp, scenario, CancelledScheduleId,
            cancellationToken);
    }

    private static async Task ConfigureAsync(KeyLoadClient sdk, MessagingClusterScenario scenario, Guid id,
        DateTimeOffset firstDueAt, CancellationToken cancellationToken)
    {
        var definition = DueRecurringClusterProtocol.Definition(scenario.SourceQueue, id, firstDueAt);
        var command = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(definition, 0));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, cancellationToken));
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);
    }

    private static async Task ConfigureAsync(McpOfficialClient mcp, MessagingClusterScenario scenario, Guid id,
        DateTimeOffset firstDueAt, CancellationToken cancellationToken)
    {
        var definition = DueRecurringClusterProtocol.Definition(scenario.SourceQueue, id, firstDueAt);
        var command = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ConfigureRecurringSchedule(definition, 0));
        var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken);
        var receipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply);
        await Assert.That(receipt.Value.Mutations).HasSingleItem();
        await Assert.That(receipt.Value.Mutations[0].Revision).IsEqualTo(1L);
    }

    private static async Task CancelAsync(McpOfficialClient mcp, MessagingClusterScenario scenario, Guid id,
        CancellationToken cancellationToken)
    {
        var command = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new CancelRecurringSchedule(scenario.SourceQueue, id, 1));
        var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken);
        var receipt = await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply);
        await Assert.That(receipt.Value.Mutations[0].Revision).IsEqualTo(2L);
    }

    private static async Task<PrincipalRecord> RevokeTemplateUseAsync(ClusterFixture fixture,
        MessagingClusterIdentity creator,
        CancellationToken cancellationToken)
    {
        var revoked = MessagingClusterIdentity.WithFieldGrants(creator.Principal, [MessagingClusterScenario.WriteGrant]);
        await MessagingClusterIdentity.UpdateAsync(fixture, revoked, cancellationToken);
        return revoked;
    }

    private static async Task RestoreTemplateUseAsync(ClusterFixture fixture, PrincipalRecord current,
        CancellationToken cancellationToken)
    {
        var restored = MessagingClusterIdentity.WithFieldGrants(current,
            [MessagingClusterScenario.WriteGrant, RecurringSagaClusterSupport.UseGrant]);
        await MessagingClusterIdentity.UpdateAsync(fixture, restored, cancellationToken);
    }
}
