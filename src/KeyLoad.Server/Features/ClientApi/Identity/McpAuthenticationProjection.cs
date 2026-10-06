using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

// Counts the existing public PrincipalRecord projection without creating JSON, strings or DTOs.
internal sealed class McpAuthenticationProjection(bool enforceMcpBounds, IOptions<McpExecutionOptions> options)
{
    private const int GetScalarPropertyCountEmptyCount = 0;
    private const int ObjectDepthSingleItemCount = 1;
    private const int GetArrayPropertyCountEmptyCount = 0;
    private const int GetArrayDepthSingleItemCount = 1;
    private const int AddDepthStep = 1;

    private readonly McpExecutionOptions settings = options.Value;
    private const int ContainerTokens = 2;
    private const int ScalarTokens = 1;
    internal const int PrincipalProperties = 11;
    internal const int GrantProperties = 3;
    private const int PrincipalScalarValues = 8;
    private const int PrincipalArrayValues = 3;
    private const int PrincipalMinimumTokens = ContainerTokens + PrincipalProperties + PrincipalScalarValues + PrincipalArrayValues * ContainerTokens;
    private const int GrantMinimumTokens = ContainerTokens + GrantProperties * (ScalarTokens + ScalarTokens);
    internal static McpFrameShape Scalar => new(ScalarTokens, GetScalarPropertyCountEmptyCount);

    internal McpFrameShape Object(int properties) => Check(new(ContainerTokens + properties, properties, ObjectDepthSingleItemCount));
    internal static McpFrameShape Array => new(ContainerTokens, GetArrayPropertyCountEmptyCount, GetArrayDepthSingleItemCount);

    internal McpFrameShape Add(McpFrameShape container, McpFrameShape child) => Check(new(
        checked(container.TokenCount + child.TokenCount), checked(container.PropertyCount + child.PropertyCount),
        Math.Max(container.Depth, checked(child.Depth + AddDepthStep))));

    internal McpFrameShape Check(McpFrameShape shape)
    {
        if (enforceMcpBounds && (shape.TokenCount > settings.MaximumTokens
            || shape.PropertyCount > settings.MaximumProperties
            || shape.Depth > settings.MaximumDepth))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        return shape;
    }

    internal void RequireCount<T>(uint count)
    {
        const int RequireCountAbsentCount = 0;

        var tokens = PrincipalMinimumTokens + (long)count * (typeof(T) == typeof(ScopeGrant) ? GrantMinimumTokens : ScalarTokens);
        var properties = PrincipalProperties + (typeof(T) == typeof(ScopeGrant) ? (long)count * GrantProperties : RequireCountAbsentCount);
        if (enforceMcpBounds && (tokens > settings.MaximumTokens || properties > settings.MaximumProperties))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
    }
}

// Native references retain only bounded projection metadata. They never retain user string bytes
// or pretend to be executable PrincipalRecord/ScopeGrant instances.
internal sealed record McpAuthenticationReference(Type NativeType, McpFrameShape Shape);
