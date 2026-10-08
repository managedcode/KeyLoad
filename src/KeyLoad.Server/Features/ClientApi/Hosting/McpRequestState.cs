using KeyLoad.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns one stateless HTTP body, current identity and borrowed structured reply through native draining.</summary>
internal sealed class McpRequestState : IDisposable
{
    private readonly McpRequestAdmission admission;
    private readonly int capacity;
    private readonly IOptions<McpExecutionOptions> options;
    private readonly McpExecutionOptions settings;
    private readonly List<McpReplyOwner> replies = [];
    private ReadOnlyMemory<byte> authentication;
    private McpFrameShape authenticationShape;
    private McpFrameBody? body;
    private bool disposed;

    internal McpRequestState(HttpAdmissionGovernor governor, McpMemoryBudget memory, int capacity, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
    {
        this.capacity = capacity;
        this.options = options;
        settings = options.Value;
        admission = new(governor: governor, memory: memory, capacity: capacity, cancellationToken: cancellationToken, options: options);
    }

    /// <summary>Gets the current database-resolved principal; native sessions cannot cache it.</summary>
    internal PrincipalRecord Principal { get; private set; } = null!;
    /// <summary>Gets the exact public capability selected before native typed decoding.</summary>
    internal McpOperationDescriptor? Operation { get; private set; }
    /// <summary>Gets the one fixed public meta operation admitted for this request.</summary>
    internal McpGatewayMetaOperation MetaOperation { get; private set; }
    /// <summary>Gets the canonical payload ceiling after admission.</summary>
    internal int MaximumPayloadBytes => admission.MaximumPayloadBytes;
    /// <summary>Gets the classified canonical result bound.</summary>
    internal int MaximumReplyBytes => admission.MaximumReplyBytes;
    internal IOptions<McpExecutionOptions> ExecutionOptions => options;

    /// <summary>Grows authentication ownership before creating the actual persisted principal DTO.</summary>
    internal void Authenticate(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        admission.CoverAuthenticationScan(capacity, reply.Length, cancellationToken);
        authenticationShape = McpNativeAuthentication.Inspect(payload: reply.Span, cancellationToken: cancellationToken, options: options);
        admission.CoverAuthentication(capacity, reply.Length, authenticationShape, cancellationToken);
        authentication = reply;
        Principal = McpNativeAuthentication.ReadAdmittedPrincipal(payload: reply.Span, cancellationToken: cancellationToken, options: options);
    }

    /// <summary>Takes exclusive replay-body lifetime ownership.</summary>
    internal void Attach(McpFrameBody ownedBody)
    {
        ArgumentNullException.ThrowIfNull(ownedBody);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (body is not null)
        { throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation); }
        body = ownedBody;
    }

    /// <summary>Admits one raw native message before typed parameter conversion.</summary>
    internal void Admit(McpGatewayMetaSelection selection, CancellationToken cancellationToken)
    {
        const int BodyWireBytesValidationBoundary = 0;
        const int NoRetainedMetadataBytes = 0;

        var descriptor = selection.CanonicalOperation;
        var input = new McpInputMemory(body?.RetainedCapacity ?? capacity, body?.WireBytes ?? BodyWireBytesValidationBoundary,
            body?.Shape ?? default,
            NoRetainedMetadataBytes, authentication.Length, authenticationShape);
        admission.Acquire(Principal, descriptor, input, cancellationToken);
        Operation = descriptor;
        MetaOperation = selection.Operation;
    }

    /// <summary>Grows before creating a canonical success wrapper and retains its borrowed native element.</summary>
    internal CallToolResult Success(DispatchedOperationReply reply, CancellationToken cancellationToken)
    {
        admission.CoverReplyScan(reply.Payload.Length, cancellationToken);
        var shape = McpFrameBounds.InspectReply(reply.Payload.Span, MaximumReplyBytes, options);
        admission.CoverReply(reply.Payload.Length, shape, cancellationToken);
        var owner = McpReplyOwner.Success(reply.Payload, reply.RequestId,
            checked(reply.Payload.Length + settings.EnvelopeAllowanceBytes), options);
        replies.Add(owner);
        return owner.ToolResult();
    }

    /// <summary>Wraps bounded gateway metadata without assigning a database execution identity.</summary>
    internal CallToolResult MetaSuccess(byte[] canonical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        try
        {
            admission.CoverReplyScan(canonical.Length, cancellationToken);
            var shape = McpFrameBounds.InspectReply(canonical, MaximumReplyBytes, options);
            admission.CoverReply(canonical.Length, shape, cancellationToken);
            var owner = McpReplyOwner.Success(canonical, null,
                checked(canonical.Length + settings.EnvelopeAllowanceBytes), options);
            var retained = false;
            try
            {
                replies.Add(owner);
                retained = true;
                return owner.ToolResult();
            }
            finally
            {
                if (!retained)
                { owner.Dispose(); }
            }
        }
        finally
        { System.Security.Cryptography.CryptographicOperations.ZeroMemory(canonical); }
    }

    /// <summary>Creates only fixed safe error content, retaining null or actual execution identity.</summary>
    internal CallToolResult Failure(ErrorCode code, Guid? requestId, string? ownedDetail = null)
    {
        var owner = McpReplyOwner.Failure(code, requestId, settings.MaximumControlReplyBytes, ownedDetail);
        replies.Add(owner);
        return owner.ToolResult();
    }

    /// <summary>Disposes payload owners before returning their complete memory and execution reservations.</summary>
    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        foreach (var reply in replies)
        { reply.Dispose(); }
        body?.Dispose();
        authentication = default;
        admission.Dispose();
    }
}
