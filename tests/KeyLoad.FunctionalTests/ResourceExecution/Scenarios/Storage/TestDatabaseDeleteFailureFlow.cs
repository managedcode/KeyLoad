using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class TestDatabaseDeleteFailureFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TestDatabaseDeleteFailurePermission.RequireUnix();
        var failures = new List<Exception>();
        TestDatabase? fixture = null;
        TestDatabaseConcurrentCloseSeed? state = null;
        PartitionIdentityInventoryCut? inventory = null;
        TestDatabaseDeleteFailurePermission? permission = null;
        UnauthorizedAccessException? first = null;
        var joinAttempted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            state = await TestDatabaseConcurrentCloseSeeding.RunAsync(fixture, TestDatabaseConcurrentCloseProtocol.OriginalId);
            joinAttempted = true;
            fixture.JoinOwnedResources();
            inventory = await TestDatabaseDeleteFailureFiles.CaptureAsync(fixture, token);
            permission = new(fixture.Directory);
            permission.DenyAndRequireEnumerationRefused();
            first = RequireDeleteFailure(fixture);
        }, failures);
        if (permission is not null)
        {
            ServerFailureObserver.Observe(permission.Restore, failures);
        }
        if (fixture is not null && !joinAttempted)
        {
            ServerFailureObserver.Observe(fixture.JoinOwnedResources, failures);
        }
        if (failures.Count == TestDatabaseDeleteFailureProtocol.NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                ArgumentNullException.ThrowIfNull(fixture);
                ArgumentNullException.ThrowIfNull(state);
                ArgumentNullException.ThrowIfNull(inventory);
                ArgumentNullException.ThrowIfNull(first);
                await RequireMemoizedAsync(fixture, inventory, first, token);
                await TestDatabaseConcurrentCloseReopen.RunAsync(fixture, state);
                Directory.Delete(fixture.Directory, recursive: true);
                await Assert.That(Directory.Exists(fixture.Directory)).IsFalse();
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await TestDatabaseDeleteFailureHealthy.RunAsync(token);
    }

    private static async Task RequireMemoizedAsync(TestDatabase fixture, PartitionIdentityInventoryCut inventory,
        UnauthorizedAccessException first, CancellationToken token)
    {
        await Assert.That(Directory.Exists(fixture.Directory)).IsTrue();
        await TestDatabaseDeleteFailureInventory.RequireAsync(fixture, inventory, token);
        var repeated = RequireDeleteFailure(fixture);
        await Assert.That(repeated).IsSameReferenceAs(first);
        await Assert.That(Directory.Exists(fixture.Directory)).IsTrue();
        await TestDatabaseDeleteFailureInventory.RequireAsync(fixture, inventory, token);
    }

    private static UnauthorizedAccessException RequireDeleteFailure(TestDatabase fixture)
    {
        try
        {
            fixture.Dispose();
        }
        catch (UnauthorizedAccessException original)
        {
            return original;
        }
        throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.DeleteMustRefuse);
    }
}
