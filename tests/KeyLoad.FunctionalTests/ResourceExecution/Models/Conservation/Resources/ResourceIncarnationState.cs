using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed record ResourceIncarnationState(Command<DatabaseCommand> Original, Result<object> Created,
    ResourceDefinition Definition, ResourceLifetime Lifetime, byte[] LifetimeBytes, byte[] CreationOutcomeBytes,
    Command<DatabaseCommand> Updated, Result<object> UpdatedResult,
    List<(Command<DatabaseCommand> Command, Result<object> Result)> Outcomes);
