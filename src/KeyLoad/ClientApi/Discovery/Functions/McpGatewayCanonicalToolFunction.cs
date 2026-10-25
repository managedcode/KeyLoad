using System.Text.Json;
using Microsoft.Extensions.AI;

namespace KeyLoad.Server;

/// <summary>Exposes one canonical schema while resolving identity and dispatch from the active request.</summary>
internal sealed class McpGatewayCanonicalToolFunction : AIFunction
{
    private const string InvalidInvocation = "The canonical MCP operation was not admitted for this request.";
    private readonly McpOperationDescriptor _operation;
    private readonly IHttpContextAccessor _httpContextAccessor;

    internal McpGatewayCanonicalToolFunction(
        McpOperationDescriptor operation,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _operation = operation;
        _httpContextAccessor = httpContextAccessor;
    }

    public override string Name => _operation.Name;
    public override string Description => _operation.Description;
    public override JsonElement JsonSchema => _operation.InputSchema;
    public override JsonElement? ReturnJsonSchema => _operation.OutputSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _httpContextAccessor.HttpContext;
        if (context is null || context.Items[McpHttpPipeline.StateItem] is not McpRequestState state || !ReferenceEquals(state.Operation, _operation))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidInvocation);
        }

        var result = await new McpToolDispatcher(context, state)
            .InvokeCanonicalAsync((ToJsonArguments(arguments)).Value, cancellationToken);
        return result;
    }

    private static (bool HasValue, Dictionary<string, JsonElement>? Value) ToJsonArguments(AIFunctionArguments arguments)
    {
        const int EmptyArgumentsCount = 0;

        if (arguments.Count == EmptyArgumentsCount)
        {
            return (false, default(Dictionary<string, JsonElement>?));
        }

        var values = new Dictionary<string, JsonElement>(arguments.Count, StringComparer.Ordinal);
        foreach (var pair in arguments)
        {
            values.Add(pair.Key, pair.Value is JsonElement element
                ? element.Clone()
                : JsonSerializer.SerializeToElement(pair.Value, pair.Value?.GetType() ?? typeof(object)));
        }

        return (true, ((Dictionary<string, JsonElement>?)(values)));
    }
}
