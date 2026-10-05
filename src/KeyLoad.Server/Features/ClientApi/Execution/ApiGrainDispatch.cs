using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class ApiGrainDispatch
{
    internal static Task<IResult> ReadAsync<T>(HttpContext context, GrainReadKind kind, T payload) =>
        ExecuteAsync(context, kind, null, Guid.Empty,
            kind is GrainReadKind.AstQuery or GrainReadKind.LiveQueryStart or GrainReadKind.LiveQueryRead
                or GrainReadKind.Traverse or GrainReadKind.GraphShortestPath or GrainReadKind.SqlGraphPath
                or GrainReadKind.AtomicPartitionPlacement or GrainReadKind.PartitionQuery
                ? NativeSerialization.Serialize(payload, NativeValidationProfile.PublicInputElements)
                : NativeSerialization.Serialize(payload));

    internal static Task<IResult> ReadAsync(HttpContext context, GrainReadKind kind) =>
        ExecuteAsync(context, kind, null, Guid.Empty, NativeSerialization.Serialize(0));

    internal static Task<IResult> SubmitAsync<T>(HttpContext context, OperationKind kind, Guid commandId, T payload) =>
        ExecuteAsync(context, null, kind, commandId, NativeSerialization.Serialize(payload, NativeValidationProfile.PublicInputElements));

    internal static Guid CommandId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers[ServerProtocol.CommandHeader], out var id) && id != Guid.Empty ? id
            : throw Errors.Fail(ErrorCode.Validation, ServerProtocol.StableCommandRequired);

    private static async Task<IResult> ExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload)
    {
        var reply = await CanonicalOperationGateway.ExecuteAsync(context, readKind, commandKind, commandId,
            payload, context.RequestAborted).ConfigureAwait(false);
        return new GrainJsonResult(reply.Payload);
    }
}
