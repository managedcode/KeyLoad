using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

// Product-specific admission of the one native authentication reply. The ordinary generated
// serializer has no configured principal token/property/array-count admission policy.
internal static class McpNativeAuthentication
{
    internal const string InvalidReply = "The native authentication reply is invalid.";
    private static readonly Lazy<NativeSerializerContext> Context = new(() => NativeSerializerProviders.Get(typeof(GrainValue)));

    internal static McpFrameShape Inspect(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
        => InspectCore(payload, options, enforceMcpBounds: true, cancellationToken: cancellationToken);

    private static McpFrameShape InspectCore(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options,
        bool enforceMcpBounds, CancellationToken cancellationToken)
    {
        RequireBytes(bytes: payload.Length, cancellationToken: cancellationToken, options: options);
        using var session = Context.Value.Sessions.GetSession();
        try
        {
            var reader = Reader.Create(payload, session);
            var inspection = new McpNativeAuthenticationReader(enforceMcpBounds: enforceMcpBounds, cancellationToken: cancellationToken, options: options);
            return inspection.Read(ref reader);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReply);
        }
        catch (Exception error) when (Malformed(error))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReply);
        }
    }

    internal static PrincipalRecord ReadPrincipal(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
    {
        // HTTP retains only its existing wire ceiling, while sharing the strict native grammar.
        _ = InspectCore(payload, options, enforceMcpBounds: false, cancellationToken: cancellationToken);
        return ReadAdmittedPrincipal(payload: payload, cancellationToken: cancellationToken, options: options);
    }

    // MCP calls only after its bounded scan and CoverAuthentication reservation have succeeded.
    internal static PrincipalRecord ReadAdmittedPrincipal(ReadOnlySpan<byte> payload, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
    {
        RequireBytes(bytes: payload.Length, cancellationToken: cancellationToken, options: options);
        var value = NativeSerialization.Deserialize<GrainValue>(payload).Value;
        return value as PrincipalRecord ?? throw Errors.Fail(ErrorCode.Corruption, InvalidReply);
    }

    private static void RequireBytes(int bytes, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options.Value.Validate();
        if (bytes > options.Value.MaximumAuthenticationBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
    }

    private static bool Malformed(Exception error) => error is SerializerException or ArgumentException
        or IndexOutOfRangeException or OverflowException or InvalidCastException or FormatException
        or EndOfStreamException or TypeLoadException || NativePayloadSyntax.IsReaderBufferFailure(error);
}
