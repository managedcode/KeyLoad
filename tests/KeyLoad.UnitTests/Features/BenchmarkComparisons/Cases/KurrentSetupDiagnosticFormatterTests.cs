using System.Text;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class KurrentSetupDiagnosticFormatterTests
{
    private const string PayloadCanary = "USER_PAYLOAD_MUST_NOT_APPEAR";
    private const string PathCanary = "ENDPOINT_SECRET_MUST_NOT_APPEAR";

    [Test]
    public async Task RealFilesystemFailureIsFormattedWithoutItsMessageOrPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), PathCanary + Guid.NewGuid().ToString("N"));
        var failure = Capture(() => File.ReadAllText(missingPath));

        var diagnostic = KurrentSetupDiagnostics.Format(KurrentSetupStage.MemberVerification, failure);

        await Assert.That(diagnostic).Contains("stage=MemberVerification");
        await Assert.That(diagnostic).Contains("FileNotFoundException");
        await Assert.That(diagnostic.Contains(PathCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(diagnostic.Contains(missingPath, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RealArithmeticFailureIsFormattedWithoutExceptionPayload()
    {
        var failure = Capture(() => Divide(0));

        var diagnostic = KurrentSetupDiagnostics.Format(KurrentSetupStage.NoStreamSemantics, failure);

        await Assert.That(diagnostic).Contains("DivideByZeroException");
        await Assert.That(diagnostic.Contains("Message", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RealCancellationFailureRetainsOnlySafeTypeAndMethodMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = Capture(cancellation.Token.ThrowIfCancellationRequested);

        var diagnostic = KurrentSetupDiagnostics.Format(KurrentSetupStage.CorpusSeeding, failure);

        await Assert.That(diagnostic).Contains("OperationCanceledException");
        await Assert.That(diagnostic.Contains("Message", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task DeepThrownInnerChainHasBoundedAsciiOutputAndNoMessages()
    {
        var failure = Capture(() => ThrowNested(12));

        var diagnostic = KurrentSetupDiagnostics.Format(KurrentSetupStage.ReplicaCopy, failure);
        var causes = diagnostic.Split("|cause[", StringSplitOptions.None).Skip(1).ToArray();

        await Assert.That(causes.Length).IsEqualTo(3);
        foreach (var cause in causes)
        {
            var frames = cause.Split("|frame[", StringSplitOptions.None).Length - 1;
            await Assert.That(frames).IsLessThanOrEqualTo(8);
        }
        await Assert.That(Encoding.ASCII.GetByteCount(diagnostic)).IsLessThanOrEqualTo(4096);
        await Assert.That(diagnostic.All(character => character is >= ' ' and <= '~')).IsTrue();
        await Assert.That(diagnostic.Contains(PayloadCanary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task MetadataIdentifiersAreAsciiAndCappedAtSixtyFourCharacters()
    {
        var identifier = KurrentSetupDiagnostics.SafeIdentifier("é\n" + new string('x', 80));

        await Assert.That(identifier.Length).IsEqualTo(64);
        await Assert.That(identifier.StartsWith("__", StringComparison.Ordinal)).IsTrue();
        await Assert.That(identifier.All(character => character is >= ' ' and <= '~')).IsTrue();
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception failure) when (failure is FileNotFoundException or DivideByZeroException
            or OperationCanceledException or InvalidOperationException)
        {
            return failure;
        }

        throw new InvalidOperationException("The test action did not fail.");
    }

    private static int Divide(int divisor) => 1 / divisor;

    private static void ThrowNested(int depth)
    {
        if (depth > 0)
        {
            try
            {
                ThrowNested(depth - 1);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException(PayloadCanary, failure);
            }
            return;
        }

        _ = Divide(0);
    }
}
