namespace KeyLoad.Core;

/// <summary>Owns node and verified-principal reservations for one admitted HTTP request.</summary>
public sealed class HttpAdmissionLease : IDisposable
{
    private const int EmptyElementCount = 0;

    private const string AlreadyBoundDetail = "The HTTP request is already bound to its verified principal.";

    private readonly Lock gate = new();
    private readonly CommandAdmissionLease nodeLease;
    private readonly CommandAdmissionGovernor scopes;
    private readonly OperationKind kind;
    private CommandAdmissionLease? scopeLease;
    private bool disposed;

    /// <summary>Gets the maximum request-body length accepted by this lease.</summary>
    public int MaxBodyBytes { get; }

    internal HttpAdmissionLease(CommandAdmissionLease nodeLease, CommandAdmissionGovernor scopes, OperationKind kind, int maxBody)
    {
        this.nodeLease = nodeLease;
        this.scopes = scopes;
        this.kind = kind;
        MaxBodyBytes = maxBody;
    }

    /// <summary>Binds the request to its verified principal exactly once.</summary>
    /// <param name="verifiedPrincipal">The authenticated principal resolved by the server.</param>
    /// <param name="cancellationToken">Cancels binding before a scope reservation is committed.</param>
    public void Bind(PrincipalRecord verifiedPrincipal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedPrincipal);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (scopeLease is not null)
            {
                throw new InvalidOperationException(AlreadyBoundDetail);
            }

            scopeLease = scopes.Reserve(kind, verifiedPrincipal, EmptyElementCount, EmptyElementCount, cancellationToken);
        }
    }

    /// <summary>Releases scope and node reservations exactly once.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            scopeLease?.Dispose();
            nodeLease.Dispose();
        }
    }
}
