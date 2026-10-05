using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeAuthenticationUnknownMetadataTests
{
    private const int RequestCapacity = 16_384;
    private const string OwnerId = "native-header-owner";
    private const string Project = "native-header-project";
    private const string FieldGrant = "native-header-field";
    private const string Resource = "native-header-resource";
    private const long PolicyEpoch = 91;
    private static readonly DateTimeOffset ExpiresAt = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Envelope)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Value)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.Member)]
    [Arguments(NativeUnknownWellKnownHeaderAuthenticationScope.ArrayCount)]
    public async Task AcNht001And002UnknownHeaderDenialPreservesValidAuthorityAndReleasesRealOwners(
        NativeUnknownWellKnownHeaderAuthenticationScope scope)
    {
        using var database = new TestDatabase();
        var expected = CreatePrincipal(McpNativeAuthenticationTests.Principal(database));
        var original = McpAuthenticationMalformedFixture.Valid(expected);
        var valid = NativeUnknownWellKnownHeaderAuthenticationFixture.Encode(original, scope, malformed: false);
        var invalid = NativeUnknownWellKnownHeaderAuthenticationFixture.Encode(original, scope, malformed: true);
        await Assert.That(valid.AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(invalid.AsSpan().SequenceEqual(valid)).IsFalse();

        var beforeInspect = McpNativeAuthentication.Inspect(valid, CancellationToken.None);
        await AssertPrincipal(McpNativeAuthentication.ReadPrincipal(valid, CancellationToken.None), expected);
        await Assert.That(beforeInspect).IsEqualTo(McpNativeAuthentication.Inspect(original, CancellationToken.None));
        await AssertDenial(() => McpNativeAuthentication.Inspect(invalid, CancellationToken.None));
        await Assert.That(McpNativeAuthentication.Inspect(valid, CancellationToken.None)).IsEqualTo(beforeInspect);
        await AssertPrincipal(McpNativeAuthentication.ReadPrincipal(valid, CancellationToken.None), expected);
        await AssertDenial(() => McpNativeAuthentication.ReadPrincipal(invalid, CancellationToken.None));
        await Assert.That(McpNativeAuthentication.Inspect(valid, CancellationToken.None)).IsEqualTo(beforeInspect);
        await AssertPrincipal(McpNativeAuthentication.ReadPrincipal(valid, CancellationToken.None), expected);

        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http());
        using (var rejected = new McpRequestState(governor, memory, RequestCapacity, CancellationToken.None))
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => rejected.Authenticate(invalid, CancellationToken.None));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo(McpNativeAuthentication.InvalidReply);
            await Assert.That(failure.Message).DoesNotContain(NativeUnknownWellKnownHeaderFixture.Canary);
        }
        await AssertReleased(governor, memory, limits);

        using (var accepted = new McpRequestState(governor, memory, RequestCapacity, CancellationToken.None))
        {
            accepted.Authenticate(valid, CancellationToken.None);
            await AssertPrincipal(accepted.Principal, expected);
        }
        await AssertReleased(governor, memory, limits);
    }

    private static PrincipalRecord CreatePrincipal(PrincipalRecord stored)
        => stored with
        {
            Id = NativeUnknownWellKnownHeaderFixture.Canary,
            Grants = [new ScopeGrant(stored.TenantId, Resource, Capability.All)],
            FieldGrants = [FieldGrant],
            ClusterAdministrator = true,
            OwnerId = OwnerId,
            Projects = [Project],
            RestrictRows = true,
            Revoked = false,
            ExpiresAt = ExpiresAt,
            PolicyEpoch = PolicyEpoch
        };

    private static async Task AssertDenial(Action decode)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(decode);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(failure.Message).IsEqualTo(McpNativeAuthentication.InvalidReply);
        await Assert.That(failure.Message).DoesNotContain(NativeUnknownWellKnownHeaderFixture.Canary);
    }

    private static async Task AssertPrincipal(PrincipalRecord actual, PrincipalRecord expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    private static async Task AssertReleased(HttpAdmissionGovernor governor, McpMemoryBudget memory, McpMemoryLimits limits)
    {
        McpNativeAuthenticationTests.AssertFullPools(memory, limits);
        var status = governor.Status();
        await Assert.That(status.Node.ControlCommands).IsEqualTo(0);
        await Assert.That(status.VerifiedScopes.ControlCommands).IsEqualTo(0);
    }
}
