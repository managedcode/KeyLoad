using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class JsonTextAllocationTests
{
    private const int ReusableResultCharacters = 8_192;
    private const int MalformedInputCharacters = 65_536;
    private const int Warmups = 8;
    private const int Iterations = 32;
    private const long PerResultAllowance = 4_096;
    private const long PerMalformedAllowance = 32_768;
    private const char PayloadCharacter = 'x';
    private const string OpeningQuote = "\"";
    private const string HealthyResultJson = "\"healthy\"";
    private const string HealthyValue = "healthy";

    [Test]
    public async Task AcMp006WarmedGetAvoidsOwnedUtf8InputPerResult()
    {
        var value = new string(PayloadCharacter, ReusableResultCharacters);
        var result = new OperationResult(JsonSerializer.Serialize(value, JsonDefaults.Options));
        for (var index = 0; index < Warmups; index++)
        {
            result.Get<string>();
        }

        var length = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Iterations; index++)
        {
            length += result.Get<string>().Length;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var maximum = Iterations * (2L * ReusableResultCharacters + PerResultAllowance);

        await Assert.That(length).IsEqualTo(Iterations * ReusableResultCharacters);
        await Assert.That(allocated).IsLessThan(maximum);
    }

    [Test]
    public async Task AcMp006MalformedParseReturnsReusableLoanAndHealthyCallStillSucceeds()
    {
        var malformed = new OperationResult(OpeningQuote + new string(PayloadCharacter, MalformedInputCharacters));
        var healthy = new OperationResult(HealthyResultJson);
        for (var index = 0; index < Warmups; index++)
        {
            ExpectMalformed(malformed);
        }

        var failures = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Iterations; index++)
        {
            try
            {
                malformed.Get<string>();
            }
            catch (JsonException)
            {
                failures++;
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(failures).IsEqualTo(Iterations);
        await Assert.That(allocated).IsLessThan(Iterations * PerMalformedAllowance);
        await Assert.That(healthy.Get<string>()).IsEqualTo(HealthyValue);
    }

    private static void ExpectMalformed(OperationResult result)
    {
        try
        {
            result.Get<string>();
        }
        catch (JsonException)
        {
            return;
        }

        throw new InvalidOperationException();
    }
}
