namespace KeyLoad.Server;

// Counts the existing public PrincipalRecord projection without creating JSON, strings or DTOs.
internal sealed class McpAuthenticationProjection(bool enforceMcpBounds)
{
    private const int ContainerTokens = 2;
    private const int ScalarTokens = 1;
    internal const int PrincipalProperties = 11;
    internal const int GrantProperties = 3;
    private const int PrincipalScalarValues = 8;
    private const int PrincipalArrayValues = 3;
    private const int PrincipalMinimumTokens = ContainerTokens + PrincipalProperties + PrincipalScalarValues + PrincipalArrayValues * ContainerTokens;
    private const int GrantMinimumTokens = ContainerTokens + GrantProperties * (ScalarTokens + ScalarTokens);
    internal static McpFrameShape Scalar => new(ScalarTokens, 0);

    internal McpFrameShape Object(int properties) => Check(new(ContainerTokens + properties, properties, 1));
    internal static McpFrameShape Array => new(ContainerTokens, 0, 1);

    internal McpFrameShape Add(McpFrameShape container, McpFrameShape child) => Check(new(
        checked(container.TokenCount + child.TokenCount), checked(container.PropertyCount + child.PropertyCount),
        Math.Max(container.Depth, checked(child.Depth + 1))));

    internal McpFrameShape Check(McpFrameShape shape)
    {
        if (enforceMcpBounds && (shape.TokenCount > McpFramingProtocol.MaximumTokens
            || shape.PropertyCount > McpFramingProtocol.MaximumProperties
            || shape.Depth > McpFramingProtocol.MaximumDepth))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        return shape;
    }

    internal void RequireCount<T>(uint count)
    {
        var tokens = PrincipalMinimumTokens + (long)count * (typeof(T) == typeof(ScopeGrant) ? GrantMinimumTokens : ScalarTokens);
        var properties = PrincipalProperties + (typeof(T) == typeof(ScopeGrant) ? (long)count * GrantProperties : 0);
        if (enforceMcpBounds && (tokens > McpFramingProtocol.MaximumTokens || properties > McpFramingProtocol.MaximumProperties))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
    }
}

// Native references retain only bounded projection metadata. They never retain user string bytes
// or pretend to be executable PrincipalRecord/ScopeGrant instances.
internal sealed record McpAuthenticationReference(Type NativeType, McpFrameShape Shape);
