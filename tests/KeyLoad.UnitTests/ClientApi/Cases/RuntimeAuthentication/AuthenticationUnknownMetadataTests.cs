using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class AuthenticationUnknownMetadataTests
{
    private const int RequestCapacity = 16_384;
    private const string OwnerId = "execution-header-owner";
    private const string Project = "execution-header-project";
    private const string FieldGrant = "execution-header-field";
    private const string Resource = "execution-header-resource";
    private const long PolicyEpoch = 91;
    private static readonly DateTimeOffset ExpiresAt = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Envelope)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Value)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.Member)]
    [Arguments(UnknownWellKnownHeaderAuthenticationScope.ArrayCount)]
    public async Task AcNht001And002UnknownHeaderDenialPreservesValidAuthorityAndReleasesRealOwners(
        UnknownWellKnownHeaderAuthenticationScope scope)
    {
        using var database = new TestDatabase();
        var expected = CreatePrincipal(McpAuthenticationTests.Principal(database));
        var original = McpAuthenticationMalformedFixture.Valid(expected);
        var valid = UnknownWellKnownHeaderAuthenticationFixture.Encode(original, scope, malformed: false);
        var invalid = UnknownWellKnownHeaderAuthenticationFixture.Encode(original, scope, malformed: true);
        await Assert.That(valid.AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(invalid.AsSpan().SequenceEqual(valid)).IsFalse();

        var beforeInspect = McpAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None);
        await AssertPrincipal(McpAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None), expected);
        await Assert.That(beforeInspect).IsEqualTo(McpAuthentication.Inspect(original, UnitMcpOptions.Execution(), CancellationToken.None));
        await AssertDenial(() => McpAuthentication.Inspect(invalid, UnitMcpOptions.Execution(), CancellationToken.None));
        await Assert.That(McpAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None)).IsEqualTo(beforeInspect);
        await AssertPrincipal(McpAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None), expected);
        await AssertDenial(() => McpAuthentication.ReadPrincipal(invalid, UnitMcpOptions.Execution(), CancellationToken.None));
        await Assert.That(McpAuthentication.Inspect(valid, UnitMcpOptions.Execution(), CancellationToken.None)).IsEqualTo(beforeInspect);
        await AssertPrincipal(McpAuthentication.ReadPrincipal(valid, UnitMcpOptions.Execution(), CancellationToken.None), expected);

        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = limits.DataBytes, ControlBytes = limits.ControlBytes, IngressBytes = limits.IngressBytes }));
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http());
        using (var rejected = new McpRequestState(governor, memory, RequestCapacity, UnitMcpOptions.Execution(), CancellationToken.None))
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => rejected.Authenticate(invalid, CancellationToken.None));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo(McpAuthentication.InvalidReply);
            await Assert.That(failure.Message).DoesNotContain(UnknownWellKnownHeaderFixture.Canary);
        }
        await AssertReleased(governor, memory, limits);

        using (var accepted = new McpRequestState(governor, memory, RequestCapacity, UnitMcpOptions.Execution(), CancellationToken.None))
        {
            accepted.Authenticate(valid, CancellationToken.None);
            await AssertPrincipal(accepted.Principal, expected);
        }
        await AssertReleased(governor, memory, limits);
    }

    private static PrincipalRecord CreatePrincipal(PrincipalRecord stored)
        => stored with
        {
            Id = UnknownWellKnownHeaderFixture.Canary,
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
        await Assert.That(failure.Message).IsEqualTo(McpAuthentication.InvalidReply);
        await Assert.That(failure.Message).DoesNotContain(UnknownWellKnownHeaderFixture.Canary);
    }

    private static async Task AssertPrincipal(PrincipalRecord actual, PrincipalRecord expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    private static async Task AssertReleased(HttpAdmissionGovernor governor, McpMemoryBudget memory, McpMemoryLimits limits)
    {
        McpAuthenticationTests.AssertFullPools(memory, limits);
        var status = governor.Status();
        await Assert.That(status.Node.ControlCommands).IsEqualTo(0);
        await Assert.That(status.VerifiedScopes.ControlCommands).IsEqualTo(0);
    }
}
