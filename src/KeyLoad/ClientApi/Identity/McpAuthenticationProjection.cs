using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

// Counts the public projection of the principal decoded by the generated Orleans codec.
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
    internal const int PrincipalProperties = 12;
    internal const int GrantProperties = 3;
    private const int PrincipalScalarValues = 8;
    private const int PrincipalArrayValues = 4;
    private const int PrincipalMinimumTokens = ContainerTokens + PrincipalProperties + PrincipalScalarValues + PrincipalArrayValues * ContainerTokens;
    private const int GrantMinimumTokens = ContainerTokens + GrantProperties * (ScalarTokens + ScalarTokens);
    internal static McpFrameShape Scalar => new(ScalarTokens, GetScalarPropertyCountEmptyCount);

    internal McpFrameShape Principal(PrincipalRecord principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shape = Object(PrincipalProperties);
        for (var index = GetScalarPropertyCountEmptyCount; index < PrincipalScalarValues; index++)
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
                for (var index = GetScalarPropertyCountEmptyCount; index < GrantProperties; index++)
                {
                    child = Add(child, Scalar);
                }
            }
            shape = Add(shape, child);
        }
        return shape;
    }

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
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        return shape;
    }

    internal void RequireCount<T>(uint count)
    {
        const int RequireCountAbsentCount = 0;

        var tokens = PrincipalMinimumTokens + (long)count * (typeof(T) == typeof(ScopeGrant) ? GrantMinimumTokens : ScalarTokens);
        var properties = PrincipalProperties + (typeof(T) == typeof(ScopeGrant) ? (long)count * GrantProperties : RequireCountAbsentCount);
        if (enforceMcpBounds && (tokens > settings.MaximumTokens || properties > settings.MaximumProperties))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
    }
}
