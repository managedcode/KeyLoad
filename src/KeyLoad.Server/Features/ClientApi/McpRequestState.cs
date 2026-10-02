using KeyLoad.Core;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns one stateless HTTP body, current identity and borrowed structured reply through native draining.</summary>
internal sealed class McpRequestState : IDisposable
{
    private readonly McpRequestAdmission admission;
    private readonly int capacity;
    private readonly List<McpReplyOwner> replies = [];
    private ReadOnlyMemory<byte> authentication;
    private McpFrameShape authenticationShape;
    private McpFrameBody? body;
    private bool disposed;

    internal McpRequestState(HttpAdmissionGovernor governor, McpMemoryBudget memory, int capacity,
        CancellationToken cancellationToken)
    {
        this.capacity = capacity;
        admission = new(governor, memory, capacity, cancellationToken);
    }

    /// <summary>Gets the current database-resolved principal; native sessions cannot cache it.</summary>
    internal PrincipalRecord Principal { get; private set; } = null!;
    /// <summary>Gets the exact public capability selected before native typed decoding.</summary>
    internal McpOperationDescriptor? Operation { get; private set; }
    /// <summary>Gets the canonical payload ceiling after admission.</summary>
    internal int MaximumPayloadBytes => admission.MaximumPayloadBytes;
    /// <summary>Gets the classified canonical result bound.</summary>
    internal int MaximumReplyBytes => admission.MaximumReplyBytes;

    /// <summary>Grows authentication ownership before creating the actual persisted principal DTO.</summary>
    internal void Authenticate(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        admission.CoverAuthenticationScan(capacity, reply.Length, cancellationToken);
        authenticationShape = McpFrameBounds.InspectValue(reply.Span, McpFramingProtocol.MaximumDataReplyBytes);
        admission.CoverAuthentication(capacity, reply.Length, authenticationShape, cancellationToken);
        authentication = reply;
        Principal = JsonDefaults.Deserialize<PrincipalRecord>(reply.Span);
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
    internal void Admit(McpOperationDescriptor? descriptor, CancellationToken cancellationToken)
    {
        var input = new McpInputMemory(body?.RetainedCapacity ?? capacity, body?.WireBytes ?? 0,
            body?.Shape ?? default,
            0, authentication.Length, authenticationShape);
        admission.Acquire(Principal, descriptor, input, cancellationToken);
        Operation = descriptor;
    }

    /// <summary>Grows before creating a canonical success wrapper and retains its borrowed native element.</summary>
    internal CallToolResult Success(DispatchedOperationReply reply, CancellationToken cancellationToken)
    {
        admission.CoverReplyScan(reply.Payload.Length, cancellationToken);
        var shape = McpFrameBounds.InspectReply(reply.Payload.Span, MaximumReplyBytes);
        admission.CoverReply(reply.Payload.Length, shape, cancellationToken);
        var owner = McpReplyOwner.Success(reply.Payload, reply.RequestId,
            checked(reply.Payload.Length + McpFramingProtocol.EnvelopeAllowanceBytes));
        replies.Add(owner);
        return owner.ToolResult();
    }

    /// <summary>Creates only fixed safe error content, retaining null or actual execution identity.</summary>
    internal CallToolResult Failure(ErrorCode code, Guid? requestId)
    {
        var owner = McpReplyOwner.Failure(code, requestId, McpFramingProtocol.MaximumControlReplyBytes);
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
