using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationFingerprintTests
{
    private const string Escaped = "\"\\\b\f\n\r\t<>&+'界λ😀\u2028\u2029";
    private const string NumericJson = "{\"large\":12345678901234567890.00,\"small\":1e-20,\"null\":null}";
    private const string Principal = "root界\\\"😀";
    private const int SegmentUnits = 4_096;
    private const int SplitCodeUnits = SegmentUnits - 1;
    private const int UnknownKind = 999;
    private static readonly Guid Id = new(NativeFingerprintFixtures.Id);

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
    public async Task Utf8AndUtf16SegmentsPreserveEscapesAndSplitUnicodeExactly(int boundary)
    {
        var prefix = new string('x', SplitCodeUnits + boundary);
        await Check(OperationKind.Batch, prefix + Escaped + prefix);
        await Check(OperationKind.Batch, prefix + NativeFingerprintFixtures.Emoji + prefix);
        await Check(OperationKind.Batch, prefix + NativeFingerprintFixtures.Unicode + prefix);
    }

    private static async Task Check(OperationKind kind, string payload)
    {
        var operation = new ReplicatedOperation(Id, kind, Principal, DateTimeOffset.UnixEpoch, payload);
        var expected = JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });
        await Assert.That(NativeOperationFingerprint.Compute(operation)).IsEqualTo(expected);
        await Assert.That(NativeOperationFingerprint.Compute(Id, kind, Principal, Encoding.UTF8.GetBytes(payload))).IsEqualTo(expected);
    }
}

internal static class NativeFingerprintFixtures
{
    internal const string Id = "01234567-89ab-cdef-0123-456789abcdef";
    internal const string Emoji = "😀";
    internal const string Unicode = "界λ";
}
