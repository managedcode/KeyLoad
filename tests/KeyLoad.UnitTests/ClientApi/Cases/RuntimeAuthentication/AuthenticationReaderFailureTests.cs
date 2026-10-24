using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class AuthenticationReaderFailureTests
{
    private const string PrivateMarker = "execution-auth-truncated-private-marker";
    private const int RequestCapacity = 16_384;

    [Test]
    public async Task GenuinePrincipalReplyRetainsTypedIdentityAndAuthorityFields()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Id = PrivateMarker
        };
        var payload = OrleansSerialization.Serialize(new GrainValue(principal));

        var shape = McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), CancellationToken.None);
        var actual = McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), CancellationToken.None);

        await Assert.That(shape.TokenCount).IsGreaterThan(0);
        await Assert.That(actual.Id).IsEqualTo(principal.Id);
        await Assert.That(actual.TenantId).IsEqualTo(principal.TenantId);
        await Assert.That(actual.Grants.SequenceEqual(principal.Grants)).IsTrue();
        await Assert.That(actual.FieldGrants.SequenceEqual(principal.FieldGrants)).IsTrue();
        await Assert.That(actual.ClusterAdministrator).IsEqualTo(principal.ClusterAdministrator);
        await Assert.That(actual.OwnerId).IsEqualTo(principal.OwnerId);
        await Assert.That(actual.Projects.SequenceEqual(principal.Projects)).IsTrue();
        await Assert.That(actual.RestrictRows).IsEqualTo(principal.RestrictRows);
        await Assert.That(actual.Revoked).IsEqualTo(principal.Revoked);
        await Assert.That(actual.ExpiresAt).IsEqualTo(principal.ExpiresAt);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
    }

    [Test]
    public async Task OneByteTruncationReturnsFixedValidationFromInspectAndReadPrincipal()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Id = PrivateMarker
        };
        var valid = OrleansSerialization.Serialize(new GrainValue(principal));
        var truncated = valid[..^1];

        await AssertSafeValidationAsync(() => McpAuthentication.Inspect(truncated, UnitMcpOptions.Execution(), CancellationToken.None));
        await AssertSafeValidationAsync(() => McpAuthentication.ReadPrincipal(truncated, UnitMcpOptions.Execution(), CancellationToken.None));
    }

    [Test]
    public async Task OneByteTruncationReleasesAllRequestPoolsAfterAuthenticationFailure()
    {
        using var database = new TestDatabase();
        var principal = McpAuthenticationTests.Principal(database) with
        {
            Id = PrivateMarker
        };
        var valid = OrleansSerialization.Serialize(new GrainValue(principal));
        var truncated = valid[..^1];
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = limits.DataBytes, ControlBytes = limits.ControlBytes, IngressBytes = limits.IngressBytes }));

        using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, UnitMcpOptions.Execution(), CancellationToken.None))
        {
            await AssertSafeValidationAsync(() => state.Authenticate(truncated, CancellationToken.None));
        }

        McpAuthenticationTests.AssertFullPools(memory, limits);
    }

    [Test]
    public async Task PreCancelledAuthenticationRemainsCancellationAndReleasesAllPools()
    {
        using var database = new TestDatabase();
        var payload = OrleansSerialization.Serialize(new GrainValue(McpAuthenticationTests.Principal(database)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(Microsoft.Extensions.Options.Options.Create(new KeyLoad.Server.McpMemoryLimits { DataBytes = limits.DataBytes, ControlBytes = limits.ControlBytes, IngressBytes = limits.IngressBytes }));

        await AssertCancelledAsync(() => McpAuthentication.Inspect(payload, UnitMcpOptions.Execution(), cancelled.Token));
        await AssertCancelledAsync(() => McpAuthentication.ReadPrincipal(payload, UnitMcpOptions.Execution(), cancelled.Token));
        using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, UnitMcpOptions.Execution(), CancellationToken.None))
        {
            await AssertCancelledAsync(() => state.Authenticate(payload, cancelled.Token));
        }

        McpAuthenticationTests.AssertFullPools(memory, limits);
    }

    private static async Task AssertSafeValidationAsync(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpAuthentication.InvalidReply);
        await Assert.That(error.Message.Contains(PrivateMarker, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertCancelledAsync(Action operation)
        => await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(operation)).IsNotNull();
}
