using System.Text.Json;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Explicit typed catalog construction; no reflection tool discovery or parallel operation engine.</summary>
internal static class McpOperationFactory
{
    internal static McpOperationDescriptor Read<TRequest, TResult>(string name, string route, GrainReadKind kind,
        bool nullableResult = false) where TRequest : notnull =>
        Create(name, route, kind, null, typeof(TRequest), typeof(TResult), nullableResult, false, McpToolHints.ForRead(kind),
            (arguments, _) => McpArgumentDecoder.Read<TRequest>(arguments, kind));

    internal static McpOperationDescriptor Read<TResult>(string name, string route, GrainReadKind kind,
        bool nullableResult = false) =>
        Create(name, route, kind, null, null, typeof(TResult), nullableResult, false, McpToolHints.ForRead(kind),
            (arguments, _) => McpArgumentDecoder.NoBody(arguments, kind));

    internal static McpOperationDescriptor Command<TRequest, TResult>(string name, string route, OperationKind kind,
        Func<TRequest, Guid> commandId) where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return Create(name, route, null, kind, typeof(TRequest), typeof(TResult), false, false, McpToolHints.ForCommand(kind),
            (arguments, _) => McpArgumentDecoder.Command(arguments, kind, commandId));
    }

    internal static McpOperationDescriptor HeaderCommand<TRequest, TResult>(string name, string route, OperationKind kind)
        where TRequest : notnull =>
        Create(name, route, null, kind, typeof(TRequest), typeof(TResult), false, true, McpToolHints.ForCommand(kind),
            (arguments, _) => McpArgumentDecoder.HeaderCommand<TRequest>(arguments, kind));

    internal static McpOperationDescriptor HeaderStatusCommand<TRequest>(string name, string route, OperationKind kind)
        where TRequest : notnull
    {
        if (kind != OperationKind.ConfigureApiKey)
        {
            throw new ArgumentException(McpCatalogProtocol.InvalidOperation, nameof(kind));
        }
        return new(name, route, null, kind, McpToolDescriptions.For(name) + McpToolDescriptions.StableRetry,
            McpSchemaFactory.CreateInput(typeof(TRequest), true),
            McpSchemaFactory.CreateOutput(typeof(object), false, true), McpToolHints.ForCommand(kind),
            (arguments, _) => McpArgumentDecoder.HeaderCommand<TRequest>(arguments, kind));
    }

    private static McpOperationDescriptor Create(string name, string route, GrainReadKind? readKind, OperationKind? commandKind,
        Type? requestType, Type resultType, bool nullableResult, bool outerCommandId, McpToolHints hints,
        Func<IDictionary<string, JsonElement>?, int, McpDecodedOperation> decoder) =>
        new(name, route, readKind, commandKind, McpToolDescriptions.For(name) +
            (commandKind.HasValue ? McpToolDescriptions.StableRetry : string.Empty),
            McpSchemaFactory.CreateInput(requestType, outerCommandId), McpSchemaFactory.CreateOutput(resultType, nullableResult),
            hints, decoder);
}
