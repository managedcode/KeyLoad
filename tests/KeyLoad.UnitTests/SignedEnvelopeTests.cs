namespace KeyLoad.UnitTests;

public sealed class SignedEnvelopeTests
{
    private sealed record Claims(string Purpose, string Payload);

    [Fact]
    public void LargerTrustedEnvelopeBudgetPreservesSmallTokenLimitAndMacValidation()
    {
        using var db = new TestDatabase();
        var claims = new Claims("command", new string('x', 16_384));
        var token = db.Database.Sign(claims);
        Assert.True(token.Length > 8_192);
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.Verify<Claims>(token)).Code);
        Assert.Equal(claims, db.Database.Verify<Claims>(token, token.Length));
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.Verify<Claims>(token, token.Length - 1)).Code);
        var signature = token.IndexOf('.') + 1;
        var tampered = token[..signature] + (token[signature] == 'A' ? 'B' : 'A') + token[(signature + 1)..];
        Assert.Equal(ErrorCode.TokenInvalidated, Assert.Throws<KeyLoadException>(() => db.Database.Verify<Claims>(tampered, token.Length)).Code);
    }
}
