using System.Globalization;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CanonicalJsonAllocationTests
{
    private const int Tokens = 2_048;
    private const int TokenCharacters = 512;
    private const int Warmups = 3;
    private const long FixedAllocationAllowance = 262_144;

    [Test]
    public async Task AcMp006WarmedFingerprintAvoidsCompleteCanonicalOutputCopies()
    {
        var values = Enumerable.Range(0, Tokens)
            .Select(index => new string('x', TokenCharacters) + index.ToString("D4", CultureInfo.InvariantCulture))
            .ToArray();
        var inputBytes = JsonDefaults.Serialize(values).Length;
        for (var index = 0; index < Warmups; index++)
        {
            JsonData.Fingerprint(values);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var fingerprint = JsonData.Fingerprint(values);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Each token is modest; a complete serialized input plus canonical output buffer
        // and output clone would exceed two input lengths after the pooled warmup.
        await Assert.That(fingerprint.Length).IsEqualTo(64);
        await Assert.That(allocated).IsLessThan(2L * inputBytes + FixedAllocationAllowance);
    }
}
