using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

// Counts the public projection of the principal decoded by the generated Orleans codec.
internal sealed class McpAuthenticationProjection(bool enforceMcpBounds, IOptions<McpExecutionOptions> options)
{
    private readonly McpExecutionOptions settings = options.Value;
    private const int ContainerTokens = 2;
    internal const int PrincipalProperties = 12;
    internal const int GrantProperties = 3;
    private const int PrincipalScalarValues = 8;
    private const int PrincipalArrayValues = 4;
    private const int PrincipalMinimumTokens = ContainerTokens + PrincipalProperties + PrincipalScalarValues + PrincipalArrayValues * ContainerTokens;
    private const int GrantMinimumTokens = ContainerTokens + GrantProperties * (1 + 1);
    internal static McpFrameShape Scalar => new(1, 0);

    internal McpFrameShape Principal(PrincipalRecord principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shape = Object(PrincipalProperties);
        for (var index = 0; index < PrincipalScalarValues; index++)
        {
            shape = Add(shape, Scalar);
        }
        shape = Add(shape, Values(principal.Grants, cancellationToken));
        shape = Add(shape, Values(principal.FieldGrants, cancellationToken));
        shape = Add(shape, Values(principal.Projects, cancellationToken));
        return Add(shape, Values(principal.Teams, cancellationToken));
    }

    private McpFrameShape Values<T>(IReadOnlyList<T> values, CancellationToken cancellationToken)
    {
        RequireCount<T>(checked((uint)values.Count));
        var shape = Array;
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var child = Scalar;
            if (value is ScopeGrant)
            {
                child = Object(GrantProperties);
                for (var index = 0; index < GrantProperties; index++)
                {
                    child = Add(child, Scalar);
                }
            }
            shape = Add(shape, child);
        }
        return shape;
    }

    internal McpFrameShape Object(int properties) => Check(new(ContainerTokens + properties, properties, 1));
    internal static McpFrameShape Array => new(ContainerTokens, 0, 1);

    internal McpFrameShape Add(McpFrameShape container, McpFrameShape child) => Check(new(
        checked(container.TokenCount + child.TokenCount), checked(container.PropertyCount + child.PropertyCount),
        Math.Max(container.Depth, checked(child.Depth + 1))));

    internal McpFrameShape Check(McpFrameShape shape)
    {
        if (enforceMcpBounds && (shape.TokenCount > settings.MaximumTokens
            || shape.PropertyCount > settings.MaximumProperties
            || shape.Depth > settings.MaximumDepth))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        return shape;
    }

    internal void RequireCount<T>(uint count)
    {
        const int RequireCountAbsentCount = 0;

        var tokens = PrincipalMinimumTokens + (long)count * (typeof(T) == typeof(ScopeGrant) ? GrantMinimumTokens : 1);
        var properties = PrincipalProperties + (typeof(T) == typeof(ScopeGrant) ? (long)count * GrantProperties : RequireCountAbsentCount);
        if (enforceMcpBounds && (tokens > settings.MaximumTokens || properties > settings.MaximumProperties))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
    }
}
