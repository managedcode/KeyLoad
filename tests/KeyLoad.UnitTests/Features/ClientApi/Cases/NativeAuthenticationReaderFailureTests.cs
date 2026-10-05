using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeAuthenticationReaderFailureTests
{
    private const string PrivateMarker = "native-auth-truncated-private-marker";
    private const int RequestCapacity = 16_384;
    private const int TruncatedByteCount = 1;

    [Test]
    public async Task GenuinePrincipalReplyRetainsTypedIdentityAndAuthorityFields()
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database) with { Id = PrivateMarker };
        var payload = NativeSerialization.Serialize(new GrainValue(principal));

        var shape = McpNativeAuthentication.Inspect(payload, CancellationToken.None);
        var actual = McpNativeAuthentication.ReadPrincipal(payload, CancellationToken.None);

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
        var principal = McpNativeAuthenticationTests.Principal(database) with { Id = PrivateMarker };
        var valid = NativeSerialization.Serialize(new GrainValue(principal));
        var truncated = valid[..^TruncatedByteCount];

        await AssertSafeValidationAsync(() => McpNativeAuthentication.Inspect(truncated, CancellationToken.None));
        await AssertSafeValidationAsync(() => McpNativeAuthentication.ReadPrincipal(truncated, CancellationToken.None));
    }

    [Test]
    public async Task OneByteTruncationReleasesAllRequestPoolsAfterAuthenticationFailure()
    {
        using var database = new TestDatabase();
        var principal = McpNativeAuthenticationTests.Principal(database) with { Id = PrivateMarker };
        var valid = NativeSerialization.Serialize(new GrainValue(principal));
        var truncated = valid[..^TruncatedByteCount];
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);

        using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, CancellationToken.None))
        {
            await AssertSafeValidationAsync(() => state.Authenticate(truncated, CancellationToken.None));
        }

        McpNativeAuthenticationTests.AssertFullPools(memory, limits);
    }

    [Test]
    public async Task PreCancelledAuthenticationRemainsCancellationAndReleasesAllPools()
    {
        using var database = new TestDatabase();
        var payload = NativeSerialization.Serialize(new GrainValue(McpNativeAuthenticationTests.Principal(database)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var limits = new McpMemoryLimits();
        var memory = new McpMemoryBudget(limits.DataBytes, limits.ControlBytes, limits.IngressBytes);

        await AssertCancelledAsync(() => McpNativeAuthentication.Inspect(payload, cancelled.Token));
        await AssertCancelledAsync(() => McpNativeAuthentication.ReadPrincipal(payload, cancelled.Token));
        using (var state = new McpRequestState(new HttpAdmissionGovernor(UnitAdmissionOptions.Http()), memory, RequestCapacity, CancellationToken.None))
        {
            await AssertCancelledAsync(() => state.Authenticate(payload, cancelled.Token));
        }

        McpNativeAuthenticationTests.AssertFullPools(memory, limits);
    }

    private static async Task AssertSafeValidationAsync(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(McpNativeAuthentication.InvalidReply);
        await Assert.That(error.Message.Contains(PrivateMarker, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertCancelledAsync(Action operation)
        => await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(operation)).IsNotNull();
}
