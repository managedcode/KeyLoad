using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueEnqueueProcessTerminalRefusal
{
    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> RunAsync(DatabaseEngine owner,
        ZoneTreeStore store, CancellationToken token)
    {
        const string refused = "refused";
        var command = new CommandRequest(Guid.NewGuid(), QueueEnqueueCrashProtocol.Partition,
            [new PutDocument(QueueEnqueueCrashProtocol.Collection, refused, QueueEnqueueCrashProtocol.HealthyPayload),
                new EnqueueMessage(QueueEnqueueCrashProtocol.Queue, QueueEnqueueCrashProtocol.Ready,
                    QueueEnqueueCrashProtocol.HealthyPayload, QueueEnqueueCrashProtocol.Headers)]);
        var operation = owner.OwnCommand(CrashDatabase.Operation(owner, OperationKind.Batch, command, command.CommandId));
        var domain = Domain(store);
        var result = owner.Apply(operation);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.Problem?.Detail).IsEqualTo(QueueEnqueueCrashProtocol.ExpectedDuplicateMessageDetail);
        
        await Assert.That(result.Value).IsNull();
        await Assert.That(Domain(store)).IsEquivalentTo(domain, CollectionOrdering.Matching);
        await Assert.That(owner.GetDocument(CrashFixtureValues.Principal, new(command.Partition, QueueEnqueueCrashProtocol.Collection, refused), cancellationToken: token)).IsNull();
        var retained = OutcomeStoreOracle.ReadPartition(store, command.Partition, CrashFixtureValues.Principal, command.CommandId)!;
        await QueueClaimAckRecoveryAssertions.EqualAsync(retained, result);
        await QueueClaimAckRecovery.ReplayAsync(owner, store, operation, result, token);
        return (operation, result);
    }

    private static string[] Domain(ZoneTreeStore store) => store.Read(view =>
        new[] { QueueEnqueueCrashProtocol.BodyFamily, QueueEnqueueCrashProtocol.MetadataFamily, QueueEnqueueCrashProtocol.CountersFamily, QueueEnqueueCrashProtocol.ReadyFamily, QueueEnqueueCrashProtocol.ScheduledFamily, QueueEnqueueCrashProtocol.LeaseFamily }.SelectMany(family =>
        {
            var page = view.Scan(KeySpace.Partition(family, QueueEnqueueCrashProtocol.Partition,
                QueueEnqueueCrashProtocol.Queue), QueueEnqueueCrashProtocol.ImageRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(QueueEnqueueCrashProtocol.TerminalDomainExceeded);
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + QueueEnqueueCrashProtocol.ImageSeparator + Convert.ToHexString(row.Value.Span));
        }).ToArray());
}
