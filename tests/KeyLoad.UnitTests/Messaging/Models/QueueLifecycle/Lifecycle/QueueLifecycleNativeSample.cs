using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleNativeSample(Command<DatabaseCommand> Operation, Result<object> Result,
    CommitToken ExpectedToken, IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
