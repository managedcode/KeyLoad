using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Hands ingress ownership to one canonical HTTP execution lease and an independent memory pool.</summary>
internal sealed class McpRequestAdmission : IDisposable
{
    private readonly HttpAdmissionGovernor governor;
    private readonly McpMemoryBudget memory;
    private readonly McpMemoryLease ingress;
    private readonly McpExecutionOptions settings;
    private readonly McpMemoryProjection projection;
    private HttpAdmissionLease? execution;
    private McpMemoryLease? operation;
    private bool disposed;

    internal McpRequestAdmission(HttpAdmissionGovernor governor, McpMemoryBudget memory, int capacity, IOptions<McpExecutionOptions> options, CancellationToken cancellationToken)
    {
        this.governor = governor;
        this.memory = memory;
        settings = options.Value;
        settings.Validate();
        projection = new(options);
        MaximumReplyBytes = settings.MaximumControlReplyBytes;
        ingress = memory.Reserve(McpMemoryLane.Ingress, projection.Ingress(capacity), cancellationToken);
    }

    /// <summary>Gets the actual canonical payload bound only after successful execution admission.</summary>
    internal int MaximumPayloadBytes => execution?.MaxBodyBytes
        ?? throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation);
    /// <summary>Gets the classified reply ceiling; unclassified failures use the bounded control ceiling.</summary>
    internal int MaximumReplyBytes { get; private set; }

    /// <summary>Covers actual persisted principal decoding before allocation.</summary>
    internal void CoverAuthentication(int capacity, int bytes, McpFrameShape shape, CancellationToken cancellationToken)
        => ingress.GrowTo(Math.Max(ingress.RetainedBytes, projection.Authentication(capacity, bytes, shape)), cancellationToken);

    /// <summary>Charges the actual bounded principal inspection before the scanner creates names or decoded strings.</summary>
    internal void CoverAuthenticationScan(int capacity, int bytes, CancellationToken cancellationToken)
        => ingress.GrowTo(projection.AuthenticationScan(capacity, bytes), cancellationToken);

    /// <summary>Acquires both replacement owners before releasing ingress; failure preserves ingress.</summary>
    internal void Acquire(PrincipalRecord principal, McpOperationDescriptor? descriptor, McpInputMemory input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (execution is not null)
        { throw new InvalidOperationException(McpCatalogProtocol.InvalidOperation); }
        var route = descriptor?.Route ?? McpToolRoutes.AdminDispatch;
        var control = IsControlRoute(route);
        var replyBytes = control ? settings.MaximumControlReplyBytes : settings.MaximumDataReplyBytes;
        HttpAdmissionLease? candidate = null;
        McpMemoryLease? supplement = null;
        var accepted = false;
        try
        {
            candidate = governor.Begin(route, input.WireBytes, cancellationToken);
            candidate.Bind(principal, cancellationToken);
            input = input with { MaximumPayloadBytes = candidate.MaxBodyBytes };
            supplement = memory.Reserve(control ? McpMemoryLane.Control : McpMemoryLane.Data,
                projection.BeforeOperation(input, replyBytes, descriptor is null), cancellationToken);
            execution = candidate;
            operation = supplement;
            MaximumReplyBytes = replyBytes;
            accepted = true;
            ingress.Dispose();
        }
        finally
        {
            if (!accepted)
            { supplement?.Dispose(); candidate?.Dispose(); }
        }
    }

    /// <summary>Preserves the complete existing reservation while covering output construction.</summary>
    internal void CoverReply(int bytes, McpFrameShape shape, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var owner = operation ?? ingress;
        owner.GrowTo(projection.AfterReply(owner.RetainedBytes, bytes, shape), cancellationToken);
    }

    /// <summary>Charges the first canonical result inspection before actual shape is known.</summary>
    internal void CoverReplyScan(int bytes, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes > MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, McpFramingProtocol.FrameBudgetExceeded); }
        var owner = operation ?? ingress;
        owner.GrowTo(projection.ReplyScan(owner.RetainedBytes, bytes), cancellationToken);
    }

    /// <summary>Releases ownership only after the native SDK transport has drained.</summary>
    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        operation?.Dispose();
        execution?.Dispose();
        ingress.Dispose();
    }

    private static bool IsControlRoute(string route) => route is McpToolRoutes.MessagesComplete
        or McpToolRoutes.SubscriptionsComplete or McpToolRoutes.AdminDispatch
        or BlobToolRoutes.AbortUpload or BlobToolRoutes.Reclaim;
}
