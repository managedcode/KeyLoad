using KeyLoad.Clustering;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Server;

internal static class ApiGrainDispatch
{
    private const int ReadAsyncValueEmptyCount = 0;

    internal static Task<IResult> ReadAsync<T>(HttpContext context, GrainReadKind kind, T payload) =>
        ExecuteAsync(context, kind, null, Guid.Empty,
            kind is GrainReadKind.AstQuery or GrainReadKind.LiveQueryStart or GrainReadKind.LiveQueryRead
                or GrainReadKind.Traverse or GrainReadKind.GraphShortestPath or GrainReadKind.GraphIncomingEdges
                or GrainReadKind.SqlGraphPath
                or GrainReadKind.PartitionQuery
                ? OrleansSerialization.Serialize(payload, ValidationProfile.PublicInputElements)
                : OrleansSerialization.Serialize(payload));

    internal static Task<IResult> ReadAsync(HttpContext context, GrainReadKind kind) =>
        ExecuteAsync(context, kind, null, Guid.Empty, OrleansSerialization.Serialize(ReadAsyncValueEmptyCount));

    internal static Task<IResult> SubmitAsync<T>(HttpContext context, OperationKind kind, Guid commandId, T payload) =>
        ExecuteAsync(context, null, kind, commandId, OrleansSerialization.Serialize(payload, ValidationProfile.PublicInputElements));

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
        await CanonicalOperationGateway.ExecuteAsync(context, null, kind, commandId,
            OrleansSerialization.Serialize(payload, ValidationProfile.PublicInputElements),
            context.RequestAborted);
        return global::ManagedCode.Communication.Result.Succeed();
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload)
    {
        var reply = await CanonicalOperationGateway.ExecuteAsync(context, readKind, commandKind, commandId,
            payload, context.RequestAborted);
        return new GrainJsonResult(reply.Payload);
    }
}
