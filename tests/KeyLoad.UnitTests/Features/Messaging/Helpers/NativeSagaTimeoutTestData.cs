using System.Globalization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class NativeSagaTimeoutTestData
{
    internal const string RootPrincipalId = "root";
    private const string SagaQueue = "jobs";
    private const string TimeoutQueue = "timeouts";
    internal const string SagaState = "{\"phase\":\"waiting-payment\"}";
    private const string CompletedState = "{\"phase\":\"done\"}";
    internal const string TimeoutPayload = "{\"action\":\"release-reservation\"}";
    internal const string TimeoutHeaders = "{\"reason\":\"deadline\"}";
    internal const string RevocableCreator = "timeout-creator";
    private const string Wildcard = "*";
    private const string GuidFormat = "N";
    private const string MessageIdPrefix = "saga-timeout-";
    private const string MessageIdSeparator = "-";
    private const string MessageIdFormat = "x16";
    private const int PolicyEpochIncrement = 1;
    private const int InitialRevision = 0;
    private const int RevisionOne = 1;
    private const int MutationWindowDivisor = 4;
    private static readonly TimeSpan DueOffset = TimeSpan.FromSeconds(2);

    internal static NativeSagaTimeoutCase CreateWaitingSaga(NativeSagaTimeoutFixture fixture, string creator,
        bool deadlineInFuture = false)
    {
        var lane = new QueueLaneRef(fixture.Database.Partition, SagaQueue);
        var timeoutLane = new QueueLaneRef(fixture.Database.Partition, TimeoutQueue);
        var now = TimeProvider.System.GetUtcNow();
        var dueAt = deadlineInFuture ? now.Add(fixture.TestProfile.CompletionTimeout) : now.Subtract(DueOffset);
        var sagaId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var timeout = new SagaTimeoutDefinition(timeoutLane, TimeoutPayload, TimeoutHeaders, TimeToLive: SagaTimeoutTtlValue);
        var create = new CommandRequest(commandId, fixture.Database.Partition,
            [new CompareExchangeSaga(lane, sagaId, InitialRevision, SagaPhase.Waiting, SagaState, dueAt, timeout)]);
        var requestTimestamp = deadlineInFuture ? now : dueAt.Subtract(DueOffset);
        _ = fixture.Database.Submit(OperationKind.Batch, create, creator, commandId, requestTimestamp).Get<CommitReceipt>();
        var hint = new DueWorkHint(DueWorkKind.Saga, lane, sagaId, creator, RevisionOne,
            DueCoordinatorFields.NoGeneration, DueCoordinatorFields.FirstOrdinal, dueAt);
        return new NativeSagaTimeoutCase(lane, timeoutLane, sagaId, dueAt, hint);
    }

    internal static void CompleteSaga(NativeSagaTimeoutFixture fixture, NativeSagaTimeoutCase saga)
    {
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, saga.Lane.Partition,
            [new CompareExchangeSaga(saga.Lane, saga.Id, RevisionOne, SagaPhase.Completed, CompletedState)]);
        _ = fixture.Database.Submit(OperationKind.Batch, command, id: commandId).Get<CommitReceipt>();
    }

    internal static void CancelSaga(NativeSagaTimeoutFixture fixture, NativeSagaTimeoutCase saga)
    {
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, saga.Lane.Partition,
            [new CompareExchangeSaga(saga.Lane, saga.Id, RevisionOne, SagaPhase.Cancelled, SagaState)]);
        _ = fixture.Database.Submit(OperationKind.Batch, command, id: commandId).Get<CommitReceipt>();
    }

    internal static void ConfigureRevocableCreator(NativeSagaTimeoutFixture fixture)
    {
        var principal = new PrincipalRecord(RevocableCreator, fixture.Database.Partition.TenantId,
            [new(Wildcard, Wildcard, Capability.All)], [Wildcard]);
        _ = fixture.Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }

    internal static void RevokeCreator(NativeSagaTimeoutFixture fixture)
    {
        var principal = fixture.Database.Store.Read(view => fixture.Database.Database.Principal(
            view, RevocableCreator, TimeProvider.System.GetUtcNow()));
        var revoked = principal with { Revoked = true, PolicyEpoch = principal.PolicyEpoch + PolicyEpochIncrement };
        _ = fixture.Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(revoked)).Get<PrincipalRecord>();
    }

    internal static DateTimeOffset MutatableNativeJobDueAt(NativeSagaTimeoutFixture fixture)
        => TimeProvider.System.GetUtcNow().Add(TimeSpan.FromTicks(
            fixture.TestProfile.CompletionTimeout.Ticks / MutationWindowDivisor));

    internal static string TimeoutMessageId(Guid sagaId, long waitingRevision)
        => string.Concat(MessageIdPrefix, sagaId.ToString(GuidFormat, CultureInfo.InvariantCulture), MessageIdSeparator,
            waitingRevision.ToString(MessageIdFormat, CultureInfo.InvariantCulture));

    internal static TimeSpan SagaTimeoutTtlValue { get; } = TimeSpan.FromHours(1);
}

internal sealed record NativeSagaTimeoutCase(QueueLaneRef Lane, QueueLaneRef TimeoutLane, Guid Id,
    DateTimeOffset Deadline, DueWorkHint Hint);
