using System.Security.Claims;
using System.Text;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsIdentityCases
{
    private const string ExtraClaimValue = "forged-administrator";
    private const string UntrustedAuthenticationType = "Untrusted";
    private const int SubjectUtf8Limit = 256;
    private const int SubjectCharacters = SubjectUtf8Limit / 2;
    private const string TwoByteSubjectCharacter = "é";

    internal static async Task AcCrs006ActualClientAndSiloConvertersCarryPersistedPrincipalAndRequestState(RequestCqrsClusterFixture fixture)
    {
        var subject = new string(TwoByteSubjectCharacter[0], SubjectCharacters);
        PersistPrincipal(fixture, subject);
        var requestId = Guid.NewGuid();
        var state = new GrainRequestContextState(requestId, Guid.Empty, fixture.ConnectionId);
        var principal = GrainIdentityContext.CreatePrincipal(subject);
        var clientPrincipalSerializer = fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<ClaimsPrincipal>>();
        var clientStateSerializer = fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<GrainRequestContextState>>();
        var clientChunkSerializer = fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var principalBytes = clientPrincipalSerializer.SerializeToArray(principal);
        var stateBytes = clientStateSerializer.SerializeToArray(state);
        var started = CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.Started(
            Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(requestId)), sequence: 1);
        var startedBytes = clientChunkSerializer.SerializeToArray(started);
        var principalRoundTrip = clientPrincipalSerializer.Deserialize(principalBytes)
            ?? throw new InvalidOperationException("The native principal serializer returned null.");
        var stateRoundTrip = clientStateSerializer.Deserialize(stateBytes)
            ?? throw new InvalidOperationException("The native request-state serializer returned null.");
        var startedRoundTrip = clientChunkSerializer.Deserialize(startedBytes)
            ?? throw new InvalidOperationException("The native stream-chunk serializer returned null.");

        await Assert.That(Encoding.UTF8.GetByteCount(subject)).IsEqualTo(SubjectUtf8Limit);
        await Assert.That(principalRoundTrip.FindFirst(ClaimTypes.NameIdentifier)?.Value).IsEqualTo(subject);
        await Assert.That(principalRoundTrip.Identities.Count()).IsEqualTo(1);
        await Assert.That(stateRoundTrip).IsEqualTo(state);
        await Assert.That(startedRoundTrip.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(startedRoundTrip.Sequence).IsEqualTo(1L);
        await Assert.That(startedRoundTrip.EventType)
            .IsEqualTo(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(CqrsStreamChunkKind.Started));
        await Assert.That(startedRoundTrip.ProgressResult?.Value?.RequestId).IsEqualTo(requestId);
        await Assert.That(startedRoundTrip.Final).IsNull();
        var priorCalls = await ProbeGrain(fixture, requestId).CapabilityCallsAsync();
        var token = SignedRead(fixture, requestId, subject);
        using var context = RequestCqrsClientContext.Set(principal, true, state);
        var observed = await Probe(fixture, requestId, token);
        await Assert.That(observed.Accepted).IsTrue();
        await Assert.That(observed.Subject).IsEqualTo(subject);
        await Assert.That(observed.RequestId).IsEqualTo(requestId);
        await Assert.That(observed.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(observed.PrincipalBytes).IsEqualTo((long)principalBytes.Length);
        await Assert.That(observed.StateBytes).IsEqualTo((long)stateBytes.Length);
        await Assert.That(observed.StartedChunkBytes).IsGreaterThan(0);
        await Assert.That(observed.CapabilityCalls).IsEqualTo(priorCalls + 1);
    }

    internal static async Task AcCrs006AbsentPrincipalIsAcceptedOnlyForSignedAuthentication(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var state = new GrainRequestContextState(requestId, Guid.Empty, fixture.ConnectionId);
        var token = fixture.Codec.CreateRead(requestId, null, GrainReadKind.Authenticate,
            NativeSerialization.Serialize(0));
        using var context = RequestCqrsClientContext.Set(null, false, state);
        var result = await Probe(fixture, requestId, token);
        await Assert.That(result.Accepted).IsTrue();
        await Assert.That(result.Subject).IsNull();
        await Assert.That(result.PrincipalBytes).IsEqualTo(0L);
    }

    internal static async Task AcCrs006PresentNullPrincipalEntryIsNotAnonymousAuthentication(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var token = fixture.Codec.CreateRead(requestId, null, GrainReadKind.Authenticate,
            NativeSerialization.Serialize(0));
        var priorCalls = await ProbeGrain(fixture, requestId).CapabilityCallsAsync();
        using var context = RequestCqrsClientContext.Set(null, false,
            new GrainRequestContextState(requestId, Guid.Empty, fixture.ConnectionId), presentNullPrincipal: true);
        var result = await Probe(fixture, requestId, token);
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(result.CapabilityCalls).IsEqualTo(priorCalls);
    }

    internal static async Task AcCrs006InvalidNativeContextIsDeniedBeforeCapability(RequestCqrsClusterFixture fixture, RequestCqrsInvalidContext invalid)
    {
        var requestId = Guid.NewGuid();
        var subject = "native-context-subject-" + Guid.NewGuid().ToString("N");
        PersistPrincipal(fixture, subject);
        var state = StateFor(invalid, requestId, fixture.ConnectionId);
        var principal = PrincipalFor(invalid, subject);
        var priorCalls = await ProbeGrain(fixture, requestId).CapabilityCallsAsync();
        var token = SignedRead(fixture, requestId, subject);
        using var context = RequestCqrsClientContext.Set(principal, invalid != RequestCqrsInvalidContext.MissingPrincipal,
            state, invalid != RequestCqrsInvalidContext.MissingState);
        var result = await Probe(fixture, requestId, token);
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Error).IsEqualTo(ExpectedError(invalid));
        await Assert.That(result.CapabilityCalls).IsEqualTo(priorCalls);
    }

    internal static async Task AcCrs006SignedRequestKeyAndForgedAuthenticationPrincipalAreDeniedBeforeCapability(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var subject = "native-signed-subject-" + Guid.NewGuid().ToString("N");
        PersistPrincipal(fixture, subject);
        var wrongId = Guid.NewGuid();
        var state = new GrainRequestContextState(requestId, Guid.Empty, fixture.ConnectionId);
        var principal = GrainIdentityContext.CreatePrincipal(subject);
        var priorCalls = await ProbeGrain(fixture, requestId).CapabilityCallsAsync();
        var signedForOtherActor = SignedRead(fixture, wrongId, subject);
        using (var context = RequestCqrsClientContext.Set(principal, true, state))
        {
            var mismatch = await Probe(fixture, requestId, signedForOtherActor);
            await Assert.That(mismatch.Accepted).IsFalse();
            await Assert.That(mismatch.Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(mismatch.CapabilityCalls).IsEqualTo(priorCalls);
        }

        var authenticateId = Guid.NewGuid();
        var authenticate = fixture.Codec.CreateRead(authenticateId, null, GrainReadKind.Authenticate,
            NativeSerialization.Serialize(0));
        using var forged = RequestCqrsClientContext.Set(principal, true,
            new GrainRequestContextState(authenticateId, Guid.Empty, fixture.ConnectionId));
        var rejected = await Probe(fixture, authenticateId, authenticate);
        await Assert.That(rejected.Accepted).IsFalse();
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(rejected.CapabilityCalls).IsEqualTo(priorCalls);
    }

    private static void PersistPrincipal(RequestCqrsClusterFixture fixture, string subject)
    {
        var record = new PrincipalRecord(subject, fixture.Database.Partition.TenantId, [], []);
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static string SignedRead(RequestCqrsClusterFixture fixture, Guid requestId, string subject)
        => fixture.Codec.CreateRead(requestId, subject, GrainReadKind.QueryCapabilities, NativeSerialization.Serialize(0));

    private static IRequestCqrsIdentityProbeGrain ProbeGrain(RequestCqrsClusterFixture fixture, Guid requestId)
        => fixture.Cluster.Client.GetGrain<IRequestCqrsIdentityProbeGrain>(requestId);

    private static Task<RequestCqrsProbeResult> Probe(RequestCqrsClusterFixture fixture, Guid requestId, string token)
        => ProbeGrain(fixture, requestId).ValidateAsync(token, requestId);

    private static GrainRequestContextState? StateFor(RequestCqrsInvalidContext invalid, Guid requestId, Guid connectionId)
        => invalid switch
        {
            RequestCqrsInvalidContext.MismatchedRequest => new(Guid.NewGuid(), Guid.Empty, connectionId),
            RequestCqrsInvalidContext.MismatchedCommand => new(requestId, Guid.NewGuid(), connectionId),
            RequestCqrsInvalidContext.MissingState => null,
            _ => new(requestId, Guid.Empty, connectionId)
        };

    private static ClaimsPrincipal? PrincipalFor(RequestCqrsInvalidContext invalid, string subject)
        => invalid switch
        {
            RequestCqrsInvalidContext.MissingPrincipal => null,
            RequestCqrsInvalidContext.ForgedSubject => GrainIdentityContext.CreatePrincipal(ExtraClaimValue),
            RequestCqrsInvalidContext.ExtraClaim => new(new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Role, ExtraClaimValue)],
                GrainRequestStreamProtocol.AuthenticationType)),
            RequestCqrsInvalidContext.SecondIdentity => new ClaimsPrincipal(
            [
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], GrainRequestStreamProtocol.AuthenticationType),
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject)], GrainRequestStreamProtocol.AuthenticationType)
            ]),
            RequestCqrsInvalidContext.WrongAuthenticationType => new(new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, subject)], UntrustedAuthenticationType)),
            _ => GrainIdentityContext.CreatePrincipal(subject)
        };

    private static ErrorCode ExpectedError(RequestCqrsInvalidContext invalid)
        => invalid is RequestCqrsInvalidContext.MissingPrincipal or RequestCqrsInvalidContext.ForgedSubject
            or RequestCqrsInvalidContext.ExtraClaim or RequestCqrsInvalidContext.SecondIdentity
            or RequestCqrsInvalidContext.WrongAuthenticationType
            ? ErrorCode.Unauthenticated
            : ErrorCode.TokenInvalidated;
}

internal enum RequestCqrsInvalidContext
{
    MissingState,
    MismatchedRequest,
    MismatchedCommand,
    MissingPrincipal,
    ForgedSubject,
    ExtraClaim,
    SecondIdentity,
    WrongAuthenticationType
}
