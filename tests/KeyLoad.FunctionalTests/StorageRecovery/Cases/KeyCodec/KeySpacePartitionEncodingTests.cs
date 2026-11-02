using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class KeySpacePartitionEncodingTests
{
    private const string Space = "document";
    private const string PartitionKey = "Case";
    private const int MaximumComponents = 256;
    private const int ScopeComponents = 5;
    private const string LiteralKey = "0150646F63756D656E7400005074656E616E740000506461746162617365000050646F6D61696E000050436173650000506974656D73000050494400FFC3A90000";
    private static readonly PartitionRef Partition = new("tenant", "database", "domain", PartitionKey);

    [Test]
    public async Task PartitionScopeKeepsLiteralBytesCaseEscapesAndEveryDynamicComponent()
    {
        await Assert.That(KeySpace.Partition(Space, Partition, "items", "ID\0é")
            .SequenceEqual(Convert.FromHexString(LiteralKey))).IsTrue();
        KeyComponent[] suffix = ["items", long.MinValue, 1.25m, double.MaxValue, DateTimeOffset.MaxValue,
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), true, KeyComponent.Null, MissingValue.Instance, new byte[] { 0, 255 }];
        KeyComponent[] complete = [Space, "tenant", "database", "domain", PartitionKey, .. suffix];
        var actual = KeySpace.Partition(Space, Partition, suffix);
        await Assert.That(actual.SequenceEqual(KeyCodec.Encode(complete))).IsTrue();
        KeyCodecValidation.Validate(actual);
        await Assert.That(KeyCodec.Decode(actual).Length).IsEqualTo(ScopeComponents + suffix.Length);
    }

    [Test]
    public async Task ScopeAndSuffixShareComponentLimitsAndRejectMissingPartition()
    {
        KeyComponent[] suffix = [.. Enumerable.Repeat(KeyComponent.Missing, MaximumComponents - ScopeComponents)];
        var maximum = KeySpace.Partition(Space, Partition, suffix);
        await Assert.That(KeyCodec.Decode(maximum).Length).IsEqualTo(MaximumComponents);
        var excessive = Assert.ThrowsExactly<KeyLoadException>(() => KeySpace.Partition(Space, Partition, [.. suffix, KeyComponent.Null]));
        await Assert.That(excessive.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => KeySpace.Partition(Space, null!, suffix));
        await Assert.That(KeySpace.Partition(Space, Partition, ReadOnlySpan<KeyComponent>.Empty))
            .IsEquivalentTo(KeyCodec.Encode(Space, "tenant", "database", "domain", PartitionKey));
        await Assert.That(KeySpace.Partition(Space, Partition, "items", "ID\0é")
            .SequenceEqual(Convert.FromHexString(LiteralKey))).IsTrue();
    }
}
