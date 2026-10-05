using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Security;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheAuthorizationTests
{
    private const string RootId = "cache-root";
    private const string RootTenant = "system";
    private const string UserId = "cache-user";
    private const string UserTenant = "cache-tenant";
    private const string RootSecret = "cache-root.credential-secret-123456";
    private const string UserSecret = "cache-user.credential-secret-654321";
    private const string SecondUserSecret = "cache-user-2.credential-secret-789012";
    private const string UserCredentialId = "cache-user";
    private const string SecondUserCredentialId = "cache-user-2";
    private static readonly DateTimeOffset BeforeExpiry = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task WarmRawPrincipalAndCredentialBytesNeverBypassExpiryOrRevocationChecks()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore(maxValueBytes: 4096);
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        var root = new PrincipalRecord(RootId, RootTenant,
            [new ScopeGrant("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true };
        var user = new PrincipalRecord(UserId, UserTenant, [], []);
        database.Bootstrap(root, DatabaseEngine.Credential(RootId, RootId, RootSecret));
        Submit(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(user), RootId);
        Submit(database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(DatabaseEngine.Credential(UserCredentialId, UserId, UserSecret,
                BeforeExpiry.AddSeconds(1))), RootId);

        await Assert.That(database.Authenticate(UserSecret, BeforeExpiry)).IsEqualTo(UserId);
        var warmed = store.GetPointCacheDiagnostics();
        var expired = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Authenticate(UserSecret, BeforeExpiry.AddSeconds(2)));
        var afterExpiry = store.GetPointCacheDiagnostics();
        Submit(database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(DatabaseEngine.Credential(SecondUserCredentialId, UserId, SecondUserSecret)), RootId);
        await Assert.That(database.Authenticate(SecondUserSecret, BeforeExpiry)).IsEqualTo(UserId);

        Submit(database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(DatabaseEngine.Credential(UserCredentialId, UserId, UserSecret) with { Revoked = true }), RootId);
        var beforeKeyRevocationAuth = store.GetPointCacheDiagnostics();
        var revoked = Assert.ThrowsExactly<KeyLoadException>(() => database.Authenticate(UserSecret, BeforeExpiry));
        var afterRevocation = store.GetPointCacheDiagnostics();
        Submit(database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(user with { Revoked = true, PolicyEpoch = user.PolicyEpoch + 1 }), RootId);
        var beforePrincipalRevocationAuth = store.GetPointCacheDiagnostics();
        var principalRevoked = Assert.ThrowsExactly<KeyLoadException>(() => database.Authenticate(SecondUserSecret, BeforeExpiry));
        var afterPrincipalRevocation = store.GetPointCacheDiagnostics();

        await Assert.That(expired.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(principalRevoked.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(warmed.Admissions).IsGreaterThanOrEqualTo(2L);
        await Assert.That(afterExpiry.Hits - warmed.Hits).IsEqualTo(1L);
        await Assert.That(afterRevocation.NativeLookups - beforeKeyRevocationAuth.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterPrincipalRevocation.Hits - beforePrincipalRevocationAuth.Hits).IsEqualTo(1L);
        await Assert.That(afterPrincipalRevocation.NativeLookups - beforePrincipalRevocationAuth.NativeLookups)
            .IsEqualTo(1L);
    }

    private static void Submit<T>(DatabaseEngine database, OperationKind kind, T payload, string principal)
    {
        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var result = database.Apply(new(Guid.NewGuid(), kind, principal, BeforeExpiry, json));
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The persisted authorization update was rejected.");
        }
    }
}
