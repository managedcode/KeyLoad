using KeyLoad.Core.InternalSerialization;

namespace KeyLoad.FunctionalTests.InternalSerialization;

[NotInParallel]
internal sealed class DatabaseCommandFingerprintTests
{
    private const string Escaped = "\"\\\b\f\n\r\t<>&+'界λ😀\u2028\u2029";
    private const string NumericJson = "{\"large\":12345678901234567890.00,\"small\":1e-20,\"null\":null}";
    private const string Principal = "root界\\\"😀";
    private const int SegmentUnits = 4_096;
    private const int SplitCodeUnits = SegmentUnits - 1;
    private const int UnknownKind = 999;
    private static readonly Guid Id = new(FingerprintFixtures.Id);

    [Test]
    public async Task EveryKindAndEscapeMatchesTheExistingGoldenFingerprintShape()
    {
        var kinds = Enum.GetValues<OperationKind>().Append((OperationKind)UnknownKind);
        foreach (var kind in kinds)
        {
            await Check(kind, string.Empty);
            await Check(kind, Escaped);
            await Check(kind, NumericJson);
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task LargeStringSegmentsPreserveEscapesAndSplitUnicodeExactly(int boundary)
    {
        var prefix = new string('x', SplitCodeUnits + boundary);
        await Check(OperationKind.Batch, prefix + Escaped + prefix);
        await Check(OperationKind.Batch, prefix + FingerprintFixtures.Emoji + prefix);
        await Check(OperationKind.Batch, prefix + FingerprintFixtures.Unicode + prefix);
    }

    private static async Task Check(OperationKind kind, string payload)
    {
        var expected = JsonData.Fingerprint(new
        {
            Id,
            Kind = kind,
            PrincipalId = Principal,
            PayloadJson = payload
        });
        await Assert.That(DatabaseCommandFingerprint.Compute(Id, kind, Principal, payload)).IsEqualTo(expected);
    }
}
