using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ComparisonExceptionTests
{
    private const string FailureCode = "EngineProbeFailed";
    private const string Secret = "credentials=do-not-report";

    [Test]
    public async Task AcBct004StandardConstructorsPreserveCodedMessageAndInnerExceptionIdentity()
    {
        var defaultFailure = new ComparisonFailureException();
        var nullMessageFailure = new ComparisonFailureException(null);
        var codedFailure = new ComparisonFailureException(FailureCode);
        var inner = new InvalidOperationException(Secret);
        var wrappedFailure = new ComparisonFailureException(FailureCode, inner);

        await Assert.That(defaultFailure.InnerException).IsNull();
        await Assert.That(string.IsNullOrEmpty(defaultFailure.Message)).IsFalse();
        await Assert.That(nullMessageFailure.InnerException).IsNull();
        await Assert.That(string.IsNullOrEmpty(nullMessageFailure.Message)).IsFalse();
        await Assert.That(codedFailure.Message).IsEqualTo(FailureCode);
        await Assert.That(wrappedFailure.Message).IsEqualTo(FailureCode);
        await Assert.That(ReferenceEquals(wrappedFailure.InnerException, inner)).IsTrue();
        await Assert.That(wrappedFailure.Message.Contains(Secret, StringComparison.Ordinal)).IsFalse();
    }
}
