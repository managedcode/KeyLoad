using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Reserves request-body, working-set, and verified-principal capacity before HTTP deserialization.</summary>
public sealed class HttpAdmissionGovernor
{
    private const char PathSeparator = '/';
    private const int InitialSequence = 0;
    private const int AdjacentElementOffset = 1;
    private const int BodyAndWorkingReservationScale = 2;

    private const string IngressIdentity = "http-ingress";
    private const string BodyLimitDetail = "The HTTP body exceeds its admission byte budget.";
    private const string DeliveryPath = "/v1/queues/delivery";
    private const string SubscriptionDeliveryPath = "/v1/subscriptions/delivery";
    private const string DispatchPath = "/v1/admin/dispatch";
    private const string AbortBlobPath = "/v1/blobs/uploads/abort";
    private const string ReclaimBlobPath = "/v1/blobs/reclaim";
    private const string QueryPath = "/v1/query";
    private const string SearchPath = "/v1/search";
    private const string GraphTraversalPath = "/v1/graph/traverse";
    private const string SeriesReadPath = "/v1/series/read";
    private const string StreamReadPath = "/v1/streams/read";
    private const string EventReadPath = "/v1/events/read";
    private const string ChangeReadPath = "/v1/changes/read";
    private const string ProjectionReadPath = "/v1/admin/projections/read";

    private static readonly PrincipalRecord Unverified = new(IngressIdentity, IngressIdentity, [], []);
    private readonly CommandAdmissionGovernor node;
    private readonly CommandAdmissionGovernor scopes;

    /// <summary>Gets the immutable HTTP admission limits used by this governor.</summary>
    public HttpAdmissionLimits Limits => limits;
    private readonly HttpAdmissionLimits limits;

    /// <summary>Creates an HTTP governor with validated limits.</summary>
    /// <param name="options">Centrally validated HTTP limits, frozen for this admission owner.</param>
    public HttpAdmissionGovernor(IOptions<HttpAdmissionLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
        node = CreateNodeGovernor(limits);
        scopes = CreateScopeGovernor(limits);
    }

    /// <summary>Validates framing and reserves request capacity before body deserialization.</summary>
    /// <param name="path">The request path used to select the bounded lane.</param>
    /// <param name="contentLength">The declared body length, or null for chunked framing.</param>
    /// <param name="cancellationToken">Cancels admission before reservation is committed.</param>
    /// <returns>A lease that must be bound to the verified principal and disposed at request end.</returns>
    public HttpAdmissionLease Begin(string path, long? contentLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = path.TrimEnd(PathSeparator);
        var control = IsControlPath(normalizedPath);
        var maxBody = control ? Limits.MaxControlBodyBytes : Limits.MaxBodyBytes;
        ValidateContentLength(contentLength, maxBody);
        var body = checked((int)(contentLength ?? maxBody));
        var working = GetWorkingSetReservation(Limits, control, normalizedPath);
        var kind = control ? OperationKind.Delivery : OperationKind.Batch;
        var held = ReserveRequest(kind, body, working, cancellationToken);
        return new(held, scopes, kind, maxBody);
    }

    /// <summary>Returns a consistent snapshot of node and verified-scope reservations.</summary>
    public HttpAdmissionStatus Status() => new(Limits, node.Snapshot(), scopes.Snapshot());

    private static CommandAdmissionGovernor CreateNodeGovernor(HttpAdmissionLimits limits)
        => CommandAdmissionGovernor.CreateDerivedHttpPool(limits.MaxRequests, limits.MaxReservedBytes,
            limits.MaxRequests, limits.MaxRequests, limits.ReservedControlRequests, limits.ReservedControlBytes,
            limits.MaxControlBodyBytes, limits.ReservedControlRequests, limits.ReservedControlRequests);

    private static CommandAdmissionGovernor CreateScopeGovernor(HttpAdmissionLimits limits)
        => CommandAdmissionGovernor.CreateDerivedHttpPool(limits.MaxRequests, limits.MaxReservedBytes,
            limits.MaxTenantRequests, limits.MaxPrincipalRequests, limits.ReservedControlRequests,
            limits.ReservedControlBytes, limits.MaxControlBodyBytes, limits.MaxTenantControlRequests,
            limits.MaxPrincipalControlRequests);

    private static bool IsControlPath(string path)
        => path.Equals(DeliveryPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(SubscriptionDeliveryPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(DispatchPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(AbortBlobPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(ReclaimBlobPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsHeavyReadPath(string path)
        => path.StartsWith(QueryPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(SearchPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(GraphTraversalPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(SeriesReadPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(TimeSeriesReadProtocol.LatestRoute, StringComparison.OrdinalIgnoreCase)
           || path.Equals(TimeSeriesReadProtocol.AggregateRoute, StringComparison.OrdinalIgnoreCase)
           || path.Equals(TimeSeriesReadProtocol.WindowsRoute, StringComparison.OrdinalIgnoreCase)
           || path.Equals(StreamReadPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(EventReadPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(ChangeReadPath, StringComparison.OrdinalIgnoreCase)
           || path.Equals(ProjectionReadPath, StringComparison.OrdinalIgnoreCase);

    private static void ValidateContentLength(long? contentLength, int maxBody)
    {
        if (contentLength is < InitialSequence || contentLength > maxBody)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, BodyLimitDetail);
        }
    }

    private static long GetWorkingSetReservation(HttpAdmissionLimits limits, bool control, string path)
        => control ? InitialSequence : IsHeavyReadPath(path) ? limits.HeavyReadReservedBytes : limits.OtherReservedBytes;

    private CommandAdmissionLease ReserveRequest(OperationKind kind, int body, long working, CancellationToken cancellationToken)
    {
        var bodyAndWorking = checked(body + (int)((working + AdjacentElementOffset) / BodyAndWorkingReservationScale));
        return node.Reserve(kind, Unverified, bodyAndWorking, body, cancellationToken);
    }
}
