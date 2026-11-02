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
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: BCL null validation accepts the operation McpOperationDescriptor reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(httpContextAccessor) /* KLD0041: BCL null validation accepts the httpContextAccessor IHttpContextAccessor reference through its object parameter. */;
        _operation = operation;
        _httpContextAccessor = httpContextAccessor;
    }

    public override string Name => _operation.Name;
    public override string Description => _operation.Description;
    public override JsonElement JsonSchema => _operation.InputSchema;
    public override JsonElement? ReturnJsonSchema => _operation.OutputSchema;

    protected override async ValueTask<object?> /* KLD0041: AIFunction requires ValueTask<object?>; this override returns the existing MCP result through that native framework signature. */ InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _httpContextAccessor.HttpContext;
        if (context is null || (((context.Items /* KLD0041: ASP.NET Items carries the request-local admitted MCP state used to authorize this native AIFunction invocation. */)[McpHttpPipeline.StateItem] /* KLD0041: ASP.NET Items carries the request-local admitted MCP state used to authorize this native AIFunction invocation. */) is not McpRequestState state /* KLD0042: Require the exact admitted McpRequestState before comparing the selected operation descriptor. */) || !(ReferenceEquals(state.Operation, _operation) /* KLD0041: Compare the admitted MCP descriptor by reference identity; CLR ReferenceEquals requires object parameters. */))
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
            values.Add(pair.Key, ((pair.Value /* KLD0041: Read this native AIFunction object-valued argument only to clone a JsonElement or encode its concrete runtime type for strict operation decoding. */) is JsonElement element /* KLD0042: Accept an existing JsonElement from the native AIFunction object-valued argument bag and clone it before decoding the concrete operation. */)
                ? element.Clone()
                : (JsonSerializer.SerializeToElement((pair.Value /* KLD0041: Read this native AIFunction object-valued argument only to clone a JsonElement or encode its concrete runtime type for strict operation decoding. */), (pair.Value /* KLD0041: Read this native AIFunction object-valued argument only to clone a JsonElement or encode its concrete runtime type for strict operation decoding. */)?.GetType() /* KLD0041: Use the concrete type of the native AIFunction argument for JSON encoding; preserve an actual null as JSON null. */ ?? typeof(object)) /* KLD0041: Encode only this native AIFunction argument using its actual runtime type before the operation descriptor validates the bounded JSON input. */));
        }

        return (true, ((Dictionary<string, JsonElement>?)(values)));
    }
}
