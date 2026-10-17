using KeyLoad.Clustering;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeSiloDiscoveryDiagnosticEndpoints
{
    private const int EmptyRequestBodyBytes = 0;
    private const int RequiredNonceParameters = 1;
    private const int RequiredNonceValues = 1;
    private const int FirstNonceValue = 0;
    private const int RequiredSiloMatches = 1;
    private const int FirstSiloMatch = 0;
    internal static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<IOptions<NodeOptions>>().Value;
        if (!options.RequestCqrsProbe.Enabled && !options.NativeDiscoveryOmission.Enabled)
        {
            return;
        }
        app.MapGet(NativeSiloDiscoveryDiagnosticProtocol.Path, Read);
        NativeCanonicalModelCutDiagnosticEndpoints.Map(app);
    }

    private static IResult Read(HttpContext context, PartitionHostApplicationOwner owner, OrleansNode node, TimeProvider clock)
    {
        var token = context.RequestAborted;
        token.ThrowIfCancellationRequested();
        var nonce = Nonce(context.Request);
        if (context.Items[ServerProtocol.PrincipalItem] is not PrincipalRecord supplied)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        var partition = owner.Borrow();
        var principal = GrainRequestAuthority.Reload(partition.Database, supplied.Id, clock);
        GrainRequestAuthority.RequireAdministrator(principal);
        var discovery = node.Discovery;
        var authentication = node.Authentication;
        var state = discovery?.Read();
        if (discovery is null || state is null || authentication is null)
        {
            throw Unavailable();
        }
        RequireCurrent(node, state);
        token.ThrowIfCancellationRequested();
        if (NativeSerialization.Measure(state) > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, NativeSiloDiscoveryDiagnosticProtocol.Unavailable);
        }
        var payload = NativeSerialization.Serialize(state);
        if (payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, NativeSiloDiscoveryDiagnosticProtocol.Unavailable);
        }
        var signature = authentication.SignDiscovery(payload, nonce);
        token.ThrowIfCancellationRequested();
        RequireCurrent(node, state);
        if (!ReferenceEquals(discovery, node.Discovery) || !ReferenceEquals(authentication, node.Authentication)
            || discovery.Read() != state)
        {
            throw Unavailable();
        }
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, NativeSiloDiscoveryDiagnosticProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        context.Response.Headers[NativeSiloDiscoveryDiagnosticProtocol.SignatureHeader] = signature;
        return Results.Bytes(payload, NativeSiloDiscoveryDiagnosticProtocol.ContentType);
    }

    private static Guid Nonce(HttpRequest request)
    {
        if (request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
            || request.ContentLength is > EmptyRequestBodyBytes || request.Query.Count != RequiredNonceParameters
            || !request.Query.TryGetValue(NativeSiloDiscoveryDiagnosticProtocol.NonceParameter, out var values)
            || values.Count != RequiredNonceValues || !Guid.TryParseExact(values[FirstNonceValue], NativeSiloDiscoveryDiagnosticProtocol.NonceFormat, out var nonce)
            || nonce == Guid.Empty || !string.Equals(values[FirstNonceValue], nonce.ToString(NativeSiloDiscoveryDiagnosticProtocol.NonceFormat),
                StringComparison.OrdinalIgnoreCase))
        {
            throw Errors.Fail(ErrorCode.Validation, NativeSiloDiscoveryDiagnosticProtocol.InvalidRequest);
        }
        return nonce;
    }

    internal static void RequireCurrent(OrleansNode node, ReplicaSiloDiscovery state)
    {
        if (!node.SiloJoined || !state.TransportReady)
        {
            throw Unavailable();
        }
        var membership = node.ReadActiveNativeMembership();
        var matching = membership.Where(value => string.Equals(value.SiloName, state.VoterId, StringComparison.Ordinal)).ToArray();
        if (matching.Length != RequiredSiloMatches || !string.Equals(matching[FirstSiloMatch].SiloAddress, state.SiloAddress, StringComparison.Ordinal))
        {
            throw Unavailable();
        }
    }

    private static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.OwnershipLost, NativeSiloDiscoveryDiagnosticProtocol.Unavailable);
}
