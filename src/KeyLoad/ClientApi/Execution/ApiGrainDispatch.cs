using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class ApiGrainDispatch
{
    internal static Task<IResult> ReadAsync<T>(HttpContext context, GrainReadKind kind, T payload) where T : notnull =>
        ExecuteAsync(context, kind, null, Guid.Empty, payload);

    internal static Task<IResult> ReadAsync(HttpContext context, GrainReadKind kind) =>
        ExecuteAsync(context, kind, null, Guid.Empty, GrainContracts.NoDtoMarker);

    internal static Task<IResult> SubmitAsync<T>(HttpContext context, OperationKind kind, Guid commandId, T payload) where T : notnull =>
        ExecuteAsync(context, null, kind, commandId, payload);

    internal static Guid CommandId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers[ServerProtocol.CommandHeader], out var id) && id != Guid.Empty ? id
            : throw Errors.Fail(ErrorCode.Validation, ServerProtocol.StableCommandRequired);

    internal static async Task<global::ManagedCode.Communication.Result> SubmitStatusAsync<T>(
        HttpContext context, OperationKind kind, Guid commandId, T payload)
    {
        if (kind != OperationKind.ConfigureApiKey)
        {
            throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest);
        }
        await CanonicalOperationGateway.ExecuteAsync(context, null, kind, commandId, payload!, context.RequestAborted);
        return global::ManagedCode.Communication.Result.Succeed();
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, object payload)
    {
        var reply = await CanonicalOperationGateway.ExecuteAsync(context, readKind, commandKind, commandId,
            payload, context.RequestAborted);
        return new GrainJsonResult(reply.Payload);
    }
}
