using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerHostEvidenceTests
{
    [Test]
    public async Task AcScale016ReadsActualBoundedLinuxHardwareAndCgroupEnvelope()
    {
        var (hardware, envelope) = await ScaleServerHostEvidence.ReadAsync(
            UnitAppHostResourceOptions.Execution(), UnitAppHostResourceOptions.Provenance(), TimeProvider.System, TestContext.Current!.Execution.CancellationToken);
        if (!OperatingSystem.IsLinux())
        {
            await Assert.That(hardware).IsNull();
            await Assert.That(envelope).IsNull();
            return;
        }
        await Assert.That(hardware).IsNotNull();
        if (hardware is null || envelope is null)
        {
            var failures = new List<Exception>
            { new InvalidOperationException("Supported Linux host evidence was unavailable.") };
            ServerFailureObserver.Observe(() => _ = ScaleServerCgroupOracle.ReadCurrent(), failures);
            var diagnostic = string.Join("; ", failures.Select(failure => failure.Message));
            throw new InvalidOperationException(diagnostic, new AggregateException(failures));
        }

        await Assert.That(hardware.LogicalCpuCount > 0 && hardware.PhysicalCoreCount > 0
            && hardware.MemoryBytes > 0 && hardware.LogicalCpuMembership.Length > 0
            && hardware.PhysicalCoreMembership.Length == hardware.PhysicalCoreCount).IsTrue();
        await Assert.That(envelope.CpuSet.Length > 0 && envelope.MemoryLimitBytes.Length > 0).IsTrue();
        await Assert.That(envelope).IsEqualTo(ScaleServerCgroupOracle.ReadCurrent());
    }
}
