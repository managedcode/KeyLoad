namespace KeyLoad.Core;

// Public request admission happens before deserialization; peer transport has its own independent limits.
public sealed class HttpAdmissionGovernor
{
    private static readonly PrincipalRecord Unverified = new("http-ingress", "http-ingress", [], []);
    private readonly CommandAdmissionGovernor node, scopes;
    public HttpAdmissionLimits Limits { get; }
    public HttpAdmissionGovernor(HttpAdmissionLimits? limits = null)
    {
        Limits = limits ?? new(); Limits.Validate();
        CommandAdmissionLimits settings = new()
        {
            MaxCommands = Limits.MaxRequests, MaxRetainedBytes = Limits.MaxReservedBytes,
            MaxTenantCommands = Limits.MaxRequests, MaxPrincipalCommands = Limits.MaxRequests,
            ReservedControlCommands = Limits.ReservedControlRequests, ReservedControlBytes = Limits.ReservedControlBytes,
            MaxControlPayloadBytes = Limits.MaxControlBodyBytes, MaxTenantControlCommands = Limits.ReservedControlRequests,
            MaxPrincipalControlCommands = Limits.ReservedControlRequests
        };
        node = new(settings);
        scopes = new(settings with { MaxTenantCommands = Limits.MaxTenantRequests, MaxPrincipalCommands = Limits.MaxPrincipalRequests,
            MaxTenantControlCommands = Limits.MaxTenantControlRequests, MaxPrincipalControlCommands = Limits.MaxPrincipalControlRequests });
    }
    private static bool Control(string path) => path.Equals("/v1/queues/delivery", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/subscriptions/delivery", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/admin/dispatch", StringComparison.OrdinalIgnoreCase);
    private static bool HeavyRead(string path) => path.StartsWith("/v1/query", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/search", StringComparison.OrdinalIgnoreCase) || path.Equals("/v1/graph/traverse", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/series/read", StringComparison.OrdinalIgnoreCase) || path.Equals("/v1/streams/read", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/events/read", StringComparison.OrdinalIgnoreCase) || path.Equals("/v1/changes/read", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/v1/admin/projections/read", StringComparison.OrdinalIgnoreCase);
    public Lease Begin(string path, long? contentLength, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); path = path.TrimEnd('/');
        var control = Control(path); var maxBody = control ? Limits.MaxControlBodyBytes : Limits.MaxBodyBytes;
        if (contentLength is < 0 || contentLength > maxBody)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The HTTP body exceeds its admission byte budget.");
        // A chunked request reserves the whole permitted body; Content-Length is a framing bound, not a client budget claim.
        var body = checked((int)(contentLength ?? maxBody));
        var working = control ? 0 : HeavyRead(path) ? Limits.HeavyReadReservedBytes : Limits.OtherReservedBytes;
        var kind = control ? OperationKind.Delivery : OperationKind.Batch;
        // The shared reservation model charges 4*body + working + envelope overhead.
        var held = node.Reserve(kind, Unverified, checked(body + (int)((working + 1) / 2)), body, cancellationToken);
        return new(held, scopes, kind, maxBody);
    }
    public HttpAdmissionStatus Status() => new(Limits, node.Snapshot(), scopes.Snapshot());
    public sealed class Lease : IDisposable
    {
        private readonly object gate = new();
        private readonly CommandAdmissionGovernor.Lease nodeLease;
        private readonly CommandAdmissionGovernor scopes;
        private readonly OperationKind kind;
        private CommandAdmissionGovernor.Lease? scopeLease;
        private bool disposed;
        public int MaxBodyBytes { get; }
        internal Lease(CommandAdmissionGovernor.Lease nodeLease, CommandAdmissionGovernor scopes, OperationKind kind, int maxBody)
        { this.nodeLease = nodeLease; this.scopes = scopes; this.kind = kind; MaxBodyBytes = maxBody; }
        public void Bind(PrincipalRecord verifiedPrincipal, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (scopeLease is not null) throw new InvalidOperationException("The HTTP request is already bound to its verified principal.");
                scopeLease = scopes.Reserve(kind, verifiedPrincipal, 0, 0, cancellationToken);
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; scopeLease?.Dispose(); nodeLease.Dispose();
            }
        }
    }
}
