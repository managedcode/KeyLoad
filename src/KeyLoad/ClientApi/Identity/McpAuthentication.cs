using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class McpAuthentication
{
    internal const string InvalidReply = "The execution authentication reply is invalid.";

    internal static McpFrameShape Inspect(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
        => Inspect(ReadPrincipal(payload, options, cancellationToken), options, cancellationToken);

    internal static McpFrameShape Inspect(PrincipalRecord principal, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
        => new McpAuthenticationProjection(true, options).Principal(principal, cancellationToken);

    internal static PrincipalRecord ReadPrincipal(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Value.Validate();
        if (payload.Length > options.Value.MaximumAuthenticationBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
        try
        {
            var value = OrleansSerialization.Deserialize<GrainValue>(payload).Value;
            cancellationToken.ThrowIfCancellationRequested();
            return value as PrincipalRecord ?? throw Errors.Fail(ErrorCode.Validation, InvalidReply);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReply);
        }
    }
}
