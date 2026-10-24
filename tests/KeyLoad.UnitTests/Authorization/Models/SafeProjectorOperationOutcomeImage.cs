namespace KeyLoad.UnitTests.Authorization;

// First-created genuine bytes, private to this fixture and never independently reencoded.
internal sealed record SafeProjectorOperationOutcomeImage(byte[] Outcome, byte[]? Locator)
{
    private const string Redacted = "SafeProjectorOperationOutcomeImage(<private>)";
    public override string ToString() => Redacted;
}
