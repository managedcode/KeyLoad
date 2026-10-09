using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerCgroupCpuFormatTests
{
    private const string NativeLimited = "100000 100000\n";
    private const string OuterWhitespaceLimited = " \t100000 100000\r\n ";
    private const string NativeUnlimited = "max 100000\n";
    private const string RootUnlimited = " \tmax\n";
    private const string LargerLimit = "200000 100000\n";
    private const string ZeroQuota = "0 100000\n";
    private const string ZeroPeriod = "100000 0\n";
    private const string MissingPeriod = "100000\n";
    private const string ExtraField = "100000 100000 100000\n";
    private const string SignedQuota = "+100000 100000\n";
    private const string OverflowPeriod = "100000 9223372036854775808\n";
    private const string InteriorNewline = "100000 \n100000\n";
    private const string InteriorTab = "100000\t100000\n";
    private const decimal OneCore = 1;

    [Test]
    [Arguments(NativeLimited)]
    [Arguments(OuterWhitespaceLimited)]
    public async Task AcScale016NativeCpuFileWhitespacePreservesPositiveLimitAndAncestorMinimum(string text)
    {
        decimal? cpu = null;
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(text, isRoot: false, ref cpu)).IsTrue();
        await Assert.That(cpu).IsEqualTo(OneCore);
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(LargerLimit, isRoot: false, ref cpu)).IsTrue();
        await Assert.That(cpu).IsEqualTo(OneCore);
    }

    [Test]
    public async Task AcScale016NativeUnlimitedAndRootWhitespacePreserveFiniteAncestor()
    {
        decimal? cpu = null;
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(NativeUnlimited, isRoot: false, ref cpu)).IsTrue();
        await Assert.That(cpu).IsNull();
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(NativeLimited, isRoot: false, ref cpu)).IsTrue();
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(RootUnlimited, isRoot: true, ref cpu)).IsTrue();
        await Assert.That(cpu).IsEqualTo(OneCore);
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(RootUnlimited, isRoot: false, ref cpu)).IsFalse();
        await Assert.That(cpu).IsEqualTo(OneCore);
    }

    [Test]
    [Arguments(ZeroQuota)]
    [Arguments(ZeroPeriod)]
    [Arguments(MissingPeriod)]
    [Arguments(ExtraField)]
    [Arguments(SignedQuota)]
    [Arguments(OverflowPeriod)]
    [Arguments(InteriorNewline)]
    [Arguments(InteriorTab)]
    public async Task AcScale016MalformedCpuFieldsRejectWithoutMutatingLimitThenHealthyFollowUp(string text)
    {
        decimal? cpu = OneCore;
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(text, isRoot: false, ref cpu)).IsFalse();
        await Assert.That(cpu).IsEqualTo(OneCore);
        await Assert.That(ScaleServerCgroupEnvelopeReader.TryAccumulateCpuLimit(NativeLimited, isRoot: false, ref cpu)).IsTrue();
        await Assert.That(cpu).IsEqualTo(OneCore);
    }
}
