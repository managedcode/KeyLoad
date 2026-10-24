using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueScheduledAuthorityState(QueueLifecycleTestState Flow, DateTimeOffset Due,
    byte[] Key, byte[] Exact, Command<DatabaseCommand> Receive, EntityRefLifetime DocumentLifetime)
{
    internal List<(ProcessingRequest Request, CommitReceipt Receipt)> Processed { get; } = [];
}
