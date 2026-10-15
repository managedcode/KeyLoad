using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class TestDatabaseConstructorRollbackContinuation
{
    private const int NoFailures = 0;
    private const long EmptyPosition = 0;

    internal static async Task RunAsync(string directory, CancellationToken token)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        var reopened = lifetime.Acquire(() => new ZoneTreeStore(new(directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
        if (reopened is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(reopened.Position).IsEqualTo(EmptyPosition);
                var identity = NativeSerialization.Serialize(reopened.Identity);
                await Assert.That(reopened.Identity.NodeId).IsNotEqualTo(Guid.Empty);
                await Assert.That(reopened.Identity.Incarnation).IsNotEqualTo(Guid.Empty);
                await Assert.That(reopened.Identity.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
                await Assert.That(TenantAdmissionAssertions.State(reopened)).IsEmpty();
                await Assert.That(NativeSerialization.Serialize(reopened.Identity).SequenceEqual(identity)).IsTrue();
                await Assert.That(reopened.Position).IsEqualTo(EmptyPosition);
            }, failures);
            lifetime.Close(reopened, failures);
        }
        if (lifetime.CanAdvance && failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.Exists(directory)).IsFalse();
        await TestDatabaseConstructorRollbackHealthy.RunAsync(directory, token);
    }
}
