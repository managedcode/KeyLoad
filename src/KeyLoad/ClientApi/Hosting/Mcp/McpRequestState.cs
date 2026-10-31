using KeyLoad.Clustering;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Owns one stateless HTTP body, current identity and borrowed structured reply through execution draining.</summary>
internal sealed class McpRequestState : IDisposable
{
    private readonly IOptions<McpExecutionOptions> options;
    private readonly McpExecutionOptions settings;
    private readonly int maximumBodyBytes;
    private readonly List<McpReplyOwner> replies = [];
    private McpFrameBody? body;
    private bool admitted;
    private bool disposed;

    internal McpRequestState(int maximumBodyBytes, IOptions<McpExecutionOptions> options)
    {
        this.maximumBodyBytes = maximumBodyBytes;
        this.options = options;
        settings = options.Value;
        settings.Validate();
        MaximumReplyBytes = settings.MaximumControlReplyBytes;
    }

    /// <summary>Gets the current database-resolved principal; execution sessions cannot cache it.</summary>
    internal PrincipalRecord Principal { get; private set; } = null!;
    /// <summary>Gets the exact public capability selected before execution typed decoding.</summary>
    internal McpOperationDescriptor? Operation
    {
        get; private set;
    }
    /// <summary>Gets the one fixed public meta operation admitted for this request.</summary>
    internal McpGatewayMetaOperation MetaOperation
    {
        get; private set;
    }
    /// <summary>Gets the canonical payload ceiling once the request selected its operation.</summary>
    internal int MaximumPayloadBytes => admitted ? maximumBodyBytes
        : throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation);
    /// <summary>Gets the classified canonical result bound; unclassified failures use the control ceiling.</summary>
    internal int MaximumReplyBytes
    {
        get; private set;
    }
    internal IOptions<McpExecutionOptions> ExecutionOptions => options;

    /// <summary>Accepts the actual persisted principal returned by authentication.</summary>
    internal void Authenticate(GrainValue reply, CancellationToken cancellationToken)
    {
        var principal = McpAuthentication.ReadPrincipal(payload: reply, cancellationToken: cancellationToken, options: options);
        _ = McpAuthentication.Inspect(principal, options, cancellationToken);
        Principal = principal;
    }

    /// <summary>Takes exclusive replay-body lifetime ownership.</summary>
    internal void Attach(McpFrameBody ownedBody)
    {
        ArgumentNullException.ThrowIfNull(ownedBody);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (body is not null)
        {
            throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation);
        }
        body = ownedBody;
    }

    /// <summary>Selects one raw execution message and its reply class before typed parameter conversion.</summary>
    internal void Admit(McpGatewayMetaSelection selection)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (admitted)
        {
            throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation);
        }
        var descriptor = selection.CanonicalOperation;
        var route = descriptor?.Route ?? McpToolRoutes.AdminDispatch;
        MaximumReplyBytes = IsControlRoute(route) ? settings.MaximumControlReplyBytes : settings.MaximumDataReplyBytes;
        admitted = true;
        Operation = descriptor;
        MetaOperation = selection.Operation;
    }

    /// <summary>Validates the canonical success reply and retains its borrowed execution element.</summary>
    internal CallToolResult Success(DispatchedOperationReply reply, CancellationToken cancellationToken)
    {
        RequireReplyBytes(reply.Payload.Length);
        _ = McpFrameBounds.InspectReply(reply.Payload.Span, MaximumReplyBytes, options);
        cancellationToken.ThrowIfCancellationRequested();
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
            RequireReplyBytes(canonical.Length);
            _ = McpFrameBounds.InspectReply(canonical, MaximumReplyBytes, options);
            cancellationToken.ThrowIfCancellationRequested();
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
                {
                    owner.Dispose();
                }
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(canonical);
        }
    }

    /// <summary>Creates only fixed safe error content, retaining null or actual execution identity.</summary>
    internal CallToolResult Failure(ErrorCode code, Guid? requestId, string? ownedDetail = null)
    {
        var owner = McpReplyOwner.Failure(code, requestId, settings.MaximumControlReplyBytes, ownedDetail);
        replies.Add(owner);
        return owner.ToolResult();
    }

    /// <summary>Disposes payload owners and the replay body.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        foreach (var reply in replies)
        {
            reply.Dispose();
        }
        body?.Dispose();
    }

    private void RequireReplyBytes(int bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes > MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded);
        }
    }

    private static bool IsControlRoute(string route) => route is McpToolRoutes.MessagesComplete
        or McpToolRoutes.SubscriptionsComplete or McpToolRoutes.AdminDispatch;
}
