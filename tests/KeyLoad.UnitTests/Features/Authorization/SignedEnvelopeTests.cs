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

    private sealed record Claims(string Purpose, string Payload);

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
        var signature = token.IndexOf(TokenSeparator.ToString(), StringComparison.Ordinal) + 1;
        var replacement = token[signature] == ReplacementSignatureCharacter
            ? AlternateSignatureCharacter
            : ReplacementSignatureCharacter;
        var tampered = token[..signature] + replacement + token[(signature + 1)..];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Verify<Claims>(tampered, token.Length)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
