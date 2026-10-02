using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Explicit typed catalog construction; no reflection tool discovery or parallel operation engine.</summary>
internal static class McpOperationFactory
{
    internal static McpOperationDescriptor Read<TRequest, TResult>(string name, string route, GrainReadKind kind,
        bool nullableResult = false) =>
        Create(name, route, kind, null, typeof(TRequest), typeof(TResult), nullableResult, false, McpToolHints.ForRead(kind),
            (arguments, limit) => McpArgumentDecoder.Read<TRequest>(arguments, kind, limit));

    internal static McpOperationDescriptor Read<TResult>(string name, string route, GrainReadKind kind,
        bool nullableResult = false) =>
        Create(name, route, kind, null, null, typeof(TResult), nullableResult, false, McpToolHints.ForRead(kind),
            (arguments, limit) => McpArgumentDecoder.NoBody(arguments, kind, limit));

    internal static McpOperationDescriptor Command<TRequest, TResult>(string name, string route, OperationKind kind,
        Func<TRequest, Guid> commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return Create(name, route, null, kind, typeof(TRequest), typeof(TResult), false, false, McpToolHints.ForCommand(kind),
            (arguments, limit) => McpArgumentDecoder.Command(arguments, kind, commandId, limit));
    }

    internal static McpOperationDescriptor HeaderCommand<TRequest, TResult>(string name, string route, OperationKind kind) =>
        Create(name, route, null, kind, typeof(TRequest), typeof(TResult), false, true, McpToolHints.ForCommand(kind),
            (arguments, limit) => McpArgumentDecoder.HeaderCommand<TRequest>(arguments, kind, limit));

    private static McpOperationDescriptor Create(string name, string route, GrainReadKind? readKind, OperationKind? commandKind,
        Type? requestType, Type resultType, bool nullableResult, bool outerCommandId, McpToolHints hints,
        Func<IDictionary<string, JsonElement>?, int, McpDecodedOperation> decoder) =>
        new(name, route, readKind, commandKind, McpToolDescriptions.For(name) +
            (commandKind.HasValue ? McpToolDescriptions.StableRetry : string.Empty),
            McpSchemaFactory.CreateInput(requestType, outerCommandId), McpSchemaFactory.CreateOutput(resultType, nullableResult),
            hints, decoder);
}
