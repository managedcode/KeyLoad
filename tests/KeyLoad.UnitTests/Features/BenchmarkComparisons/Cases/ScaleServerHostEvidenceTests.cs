using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerHostEvidenceTests
{
    [Test]
    public async Task AcScale016ReadsActualBoundedLinuxHardwareAndCgroupEnvelope()
    {
        var (hardware, envelope) = await ScaleServerHostEvidence.ReadAsync(
            UnitAppHostResourceOptions.Execution(), UnitAppHostResourceOptions.Provenance(), TestContext.Current!.Execution.CancellationToken);
        if (!OperatingSystem.IsLinux())
        {
            await Assert.That(hardware).IsNull();
            await Assert.That(envelope).IsNull();
            return;
        }
        await Assert.That(hardware).IsNotNull();
        if (hardware is null || envelope is null)
        {
            throw new InvalidOperationException("Supported Linux host evidence was unavailable.");
        }

        await Assert.That(hardware.LogicalCpuCount > 0 && hardware.PhysicalCoreCount > 0
            && hardware.MemoryBytes > 0 && hardware.LogicalCpuMembership.Length > 0
            && hardware.PhysicalCoreMembership.Length == hardware.PhysicalCoreCount).IsTrue();
        await Assert.That(envelope.CpuSet.Length > 0 && envelope.MemoryLimitBytes.Length > 0).IsTrue();
        await Assert.That(envelope).IsEqualTo(ScaleServerCgroupOracle.ReadCurrent());
    }
}
