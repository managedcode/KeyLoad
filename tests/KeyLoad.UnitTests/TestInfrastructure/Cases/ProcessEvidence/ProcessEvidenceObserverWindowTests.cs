using KeyLoad.AppHost.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.TestInfrastructure;

internal sealed class ProcessEvidenceObserverWindowTests
{
    private const string ClientName = "unit-client";
    private const string ForeignAssemblySha256 = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly TimeSpan InsideWindow = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan OutsideWindow = TimeSpan.FromMilliseconds(1_100);

    [Test]
    public async Task CurrentProcessVerifiesInsideTheObserverStartTimeWindowAndRefusesOutsideItOrWithForeignFiles()
    {
        var options = Options.Create(new TestExecutionOptions());
        var evidence = ProjectProcessEvidence.ReadCurrent(ClientName);
        ProjectProcessEvidence.Verify(evidence, options);
        ProjectProcessEvidence.Verify(evidence with { StartedAtUtc = evidence.StartedAtUtc + InsideWindow }, options);
        ProjectProcessEvidence.Verify(evidence with { StartedAtUtc = evidence.StartedAtUtc - InsideWindow }, options);
        var later = evidence with { StartedAtUtc = evidence.StartedAtUtc + OutsideWindow };
        var earlier = evidence with { StartedAtUtc = evidence.StartedAtUtc - OutsideWindow };
        var foreign = evidence with { Binary = evidence.Binary with { AssemblySha256 = ForeignAssemblySha256 } };
        await Assert.That(Assert.ThrowsExactly<InvalidDataException>(() => ProjectProcessEvidence.Verify(later, options)).Message)
            .IsEqualTo(Assert.ThrowsExactly<InvalidDataException>(() => ProjectProcessEvidence.Verify(earlier, options)).Message);
        await Assert.That(Assert.ThrowsExactly<InvalidDataException>(() => ProjectProcessEvidence.Verify(foreign, options)).Message)
            .IsEqualTo(Assert.ThrowsExactly<InvalidDataException>(() => ProjectProcessEvidence.Verify(later, options)).Message);
        await Assert.That(options.Value.ProcessStartTimeObserverWindow).IsGreaterThan(InsideWindow).And.IsLessThan(OutsideWindow);
        await Assert.That(evidence.ProcessId).IsEqualTo(Environment.ProcessId);
    }
}
