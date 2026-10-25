using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueLifecycleObservation(Command<DatabaseCommand> Operation, Result<object> Result,
    CommitToken ExpectedToken, IReadOnlyDictionary<string, MessageMetadata> Before, QueueCounters? Counters);
