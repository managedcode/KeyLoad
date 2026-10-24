using System.Security.Cryptography;
using KeyLoad.Core.InternalSerialization;

namespace KeyLoad.UnitTests.InternalSerialization;

internal sealed class CoreSignedClaimsTests
{
    private const string Queue = "execution-claims-queue";
    private const string MessageId = "execution-claims-message";
    private const string PrincipalId = "root";
    private const char Separator = '.';
    private const char SignatureA = 'A';
    private const char SignatureB = 'B';
    private const char Padding = '=';
    private const char StandardPlus = '+';
    private const char StandardSlash = '/';
    private const char UrlMinus = '-';
    private const char UrlUnderscore = '_';

    [Test]
    public async Task AcIs007ClaimsRoundTripAndTamperingOrWrongKeyRejects()
    {
        using var database = new TestDatabase();
        var claims = Claims(database);
        var token = database.Database.Sign(claims);
        await Assert.That(token.StartsWith(CoreClaims.Prefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(database.Database.Verify<DeliveryClaims>(token)).IsEqualTo(claims);
        var signature = token.LastIndexOf(Separator) + 1;
        var replacement = token[signature] == SignatureA ? SignatureB : SignatureA;
        var changed = token[..signature] + replacement + token[(signature + 1)..];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<DeliveryClaims>(changed)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        using var other = new TestDatabase();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => other.Database.Verify<DeliveryClaims>(token)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task AcIs007OldValidJsonTokenAndRemovedPrefixInvalidateExplicitly()
    {
        using var database = new TestDatabase();
        var bytes = JsonDefaults.Serialize(Claims(database));
        var old = Base64Url(bytes) + Separator
            + Base64Url(HMACSHA256.HashData(database.Store.Identity.SigningKey.Span, bytes));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<DeliveryClaims>(old)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        var execution = database.Database.Sign(Claims(database));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.Verify<DeliveryClaims>(execution[CoreClaims.Prefix.Length..])).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static DeliveryClaims Claims(TestDatabase database)
        => new(new(database.Partition, Queue), MessageId, PrincipalId, 1, 1, database.Store.Identity.Incarnation);
    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd(Padding).Replace(StandardPlus, UrlMinus).Replace(StandardSlash, UrlUnderscore);
}
