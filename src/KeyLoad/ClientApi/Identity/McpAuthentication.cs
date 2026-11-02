using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class McpAuthentication
{
    internal const string InvalidReply = "The execution authentication reply is invalid.";

    internal static McpFrameShape Inspect(GrainValue payload, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
        => Inspect(ReadPrincipal(payload, options, cancellationToken), options, cancellationToken);

    internal static McpFrameShape Inspect(PrincipalRecord principal, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
        => new McpAuthenticationProjection(true, options).Principal(principal, cancellationToken);

    internal static PrincipalRecord ReadPrincipal(GrainValue payload, IOptions<McpExecutionOptions> options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Value.Validate();
        return ((payload.Value /* KLD0041: Read the current generated GrainValue authentication slot before validating its concrete principal type. */) as PrincipalRecord /* KLD0042: Require the concrete PrincipalRecord from the existing generated authentication reply before trusting its fields. */) ?? throw Errors.Fail(ErrorCode.Validation, InvalidReply);
    }
}
