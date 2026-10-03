using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Server;

// Product-specific admission of the one native authentication reply. The ordinary generated
// serializer has no configured principal token/property/array-count admission policy.
internal static class McpNativeAuthentication
{
    internal const string InvalidReply = "The native authentication reply is invalid.";
    private static readonly Lazy<NativeSerializerContext> Context = new(() => NativeSerializerProviders.Get(typeof(GrainValue)));

    internal static McpFrameShape Inspect(ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
        => InspectCore(payload, enforceMcpBounds: true, cancellationToken: cancellationToken);

    private static McpFrameShape InspectCore(ReadOnlySpan<byte> payload, bool enforceMcpBounds, CancellationToken cancellationToken)
    {
        RequireBytes(payload.Length, cancellationToken);
        using var session = Context.Value.Sessions.GetSession();
        try
        {
            var reader = Reader.Create(payload, session);
            var inspection = new McpNativeAuthenticationReader(enforceMcpBounds, cancellationToken);
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

    internal static PrincipalRecord ReadPrincipal(ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
    {
        // HTTP retains only its existing wire ceiling, while sharing the strict native grammar.
        _ = InspectCore(payload, enforceMcpBounds: false, cancellationToken: cancellationToken);
        return ReadAdmittedPrincipal(payload, cancellationToken);
    }

    // MCP calls only after its bounded scan and CoverAuthentication reservation have succeeded.
    internal static PrincipalRecord ReadAdmittedPrincipal(ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
    {
        RequireBytes(payload.Length, cancellationToken);
        var value = NativeSerialization.Deserialize<GrainValue>(payload).Value;
        return value as PrincipalRecord ?? throw Errors.Fail(ErrorCode.Corruption, InvalidReply);
    }

    private static void RequireBytes(int bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes > McpFramingProtocol.MaximumDataReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
    }

    private static bool Malformed(Exception error) => error is SerializerException or ArgumentException
        or IndexOutOfRangeException or OverflowException or InvalidCastException or FormatException
        or EndOfStreamException or TypeLoadException;
}
