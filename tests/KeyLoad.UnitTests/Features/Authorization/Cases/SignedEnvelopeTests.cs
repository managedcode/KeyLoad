namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class SignedEnvelopeTests
{
    private const int PayloadLength = 16_384;
    private const int OriginalTokenLimit = 8_192;
    private const char PayloadCharacter = 'x';
    private const string CommandPurpose = "command";
    private const char TokenSeparator = '.';
    private const char ReplacementSignatureCharacter = 'A';
    private const char AlternateSignatureCharacter = 'B';

    private const string ClaimsAlias = "keyload.tests.v1.SignedEnvelopeClaims";
    private const uint PurposeId = 0;
    private const uint PayloadId = 1;

    [global::Orleans.GenerateSerializer]
    [global::Orleans.Alias(ClaimsAlias)]
    internal sealed record Claims([property: global::Orleans.Id(PurposeId)] string Purpose,
        [property: global::Orleans.Id(PayloadId)] string Payload);

    [Test]
    public async Task LargerTrustedEnvelopeBudgetPreservesSmallTokenLimitAndMacValidation()
    {
        using var database = new TestDatabase();
        var claims = new Claims(CommandPurpose, new string(PayloadCharacter, PayloadLength));
        var token = database.Database.Sign(claims);
        await Assert.That(token.Length > OriginalTokenLimit).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<Claims>(token)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(database.Database.Verify<Claims>(token, token.Length)).IsEqualTo(claims);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<Claims>(token, token.Length - 1)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        var signature = token.LastIndexOf(TokenSeparator) + 1;
        var replacement = token[signature] == ReplacementSignatureCharacter
            ? AlternateSignatureCharacter
            : ReplacementSignatureCharacter;
        var tampered = token[..signature] + replacement + token[(signature + 1)..];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<Claims>(tampered, token.Length)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
