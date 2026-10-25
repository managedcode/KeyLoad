using System.Text.Json;
using KeyLoad.Storage;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class SignedClaimsExecutionPolicyFlow
{
    internal const string QueueName = "signed-claims-policy-queue";
    internal const string MessageId = "signed-claims-policy-message";
    internal const string PrincipalId = "root";
    internal const string Payload = "{\"value\":\"persisted payload\"}";
    internal const string Headers = "{\"source\":\"persisted headers\"}";

    internal static Delivery Lease(TestDatabase database)
    {
        database.Configure(QueueName, ResourceKind.WorkQueue);
        database.Commit(new EnqueueMessage(QueueName, MessageId, Payload, Headers));
        var requestId = Guid.NewGuid();
        return database.Submit(OperationKind.Receive,
            new ReceiveRequest(requestId, Lane(database)), id: requestId).Get<ReceiveResult>().Deliveries.Single();
    }

    // This engine borrows the same genuine store; only its execution claim admission snapshot differs.
    internal static DatabaseEngine Borrow(TestDatabase database, IOptions<ClaimsExecutionOptions> claimsOptions)
        => new(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            database.Database.GraphOptions, UnitExecutionOptions.ChangeFeedExecution(),
            claimsOptions,
            UnitExecutionOptions.TimeSeriesExecution(), database.Database.EvaluationClock);

    internal static Result<object> Acknowledge(DatabaseEngine engine, QueueLaneRef lane, Delivery delivery)
    {
        var commandId = Guid.NewGuid();
        var command = new DeliveryCommand(commandId, lane, delivery.Token, DeliveryAction.Ack);
        return engine.Apply(engine.CreateCommand(OperationKind.Delivery, commandId, PrincipalId, engine.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)));
    }

    internal static byte[][] CaptureLease(TestDatabase database, Delivery delivery)
    {
        byte[][] keys =
        [
            KeySpace.Partition("message-meta", database.Partition, QueueName, MessageId),
            KeySpace.Partition("message-body", database.Partition, QueueName, MessageId),
            KeySpace.Partition("queue-counters", database.Partition, QueueName),
            LeaseKey(database, delivery)
        ];
        return database.Store.Read(view => keys.Select(key => view.ReadOwnedValue(key)
            ?? throw new InvalidOperationException("The genuine leased message record must exist.")).ToArray());
    }

    internal static QueueCounters Counters(TestDatabase database)
        => database.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition("queue-counters", database.Partition, QueueName))
            ?? throw new InvalidOperationException("The genuine queue counters must exist."));

    internal static byte[] LeaseKey(TestDatabase database, Delivery delivery)
        => KeySpace.Partition("lease", database.Partition, QueueName, delivery.LeaseUntil, MessageId);

    internal static QueueLaneRef Lane(TestDatabase database) => new(database.Partition, QueueName);
}
