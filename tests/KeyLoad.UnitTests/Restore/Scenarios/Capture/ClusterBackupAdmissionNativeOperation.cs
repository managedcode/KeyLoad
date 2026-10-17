using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ClusterBackupAdmissionNativeOperation
{
    internal static Task RunAsync(Func<ClusterBackupAdmissionNativeFixture, byte[], Task> body)
        => PartitionHostRecoveryFixture.RunAsync(async files =>
        {
            byte[]? original = null;
            string? principal = null;
            await files.RunHostAsync(async host =>
            {
                var failures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await using var fixture = await ClusterBackupAdmissionNativeFixture.CreateAsync(files, host);
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        original = fixture.ReadCompleteImage();
                        principal = fixture.Principal;
                        await body(fixture, original);
                    }, failures);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
            });
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(principal);
            await files.RunHostAsync(cold => ClusterBackupAdmissionColdAssertions.RequireAsync(
                files, cold, original, principal));
        });
}
