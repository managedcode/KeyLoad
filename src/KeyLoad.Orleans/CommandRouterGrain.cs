using KeyLoad.Core;

namespace KeyLoad.Orleans;

public interface ICommandRouterGrain : IGrainWithStringKey
{
    Task<string> ExecuteAsync(string envelope);
}
public sealed record GrainCommandEnvelope(string Purpose, OperationKind Kind, Guid Id, string PrincipalId,
    string PayloadJson, DateTimeOffset ExpiresAt);

[global::Orleans.Concurrency.Reentrant]
[global::Orleans.Concurrency.StatelessWorker(1)]
public sealed class CommandRouterGrain(DatabaseEngine database, ICommitCoordinator coordinator) : Grain, ICommandRouterGrain
{
    public async Task<string> ExecuteAsync(string envelope)
    {
        if (this.GetPrimaryKeyString() != "shard:0") throw Errors.Fail(ErrorCode.OwnershipLost, "The physical shard route is unavailable.");
        var command = database.Verify<GrainCommandEnvelope>(envelope);
        if (command.Purpose != "grain-command" || command.ExpiresAt < DateTimeOffset.UtcNow || command.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The internal command envelope has expired or has a different purpose.");
        var result = await coordinator.SubmitAsync(command.Kind, command.Id, command.PrincipalId, command.PayloadJson);
        return System.Text.Json.JsonSerializer.Serialize(result, JsonDefaults.Options);
    }
}
