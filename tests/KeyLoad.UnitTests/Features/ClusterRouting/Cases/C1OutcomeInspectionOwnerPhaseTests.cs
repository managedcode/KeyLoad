using System.ComponentModel;
using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionOwnerPhaseTests
{
    private const int EagainLinux = 11;
    private const int EagainMac = 35;
    private const string UnexpectedNativeErrorMessage = "The held owner lock returned an unexpected native error.";

    [Test]
    public async Task AcCrsDiag004HeldNativeOwnerLockReportsAllowlistedCodeThenRecovers()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            using var held = OfflineRegularFile.Open(fixture.OuterOwnerLockPath,
                FileAccess.ReadWrite, FileShare.None, 1024);
            var error = (await Assert.ThrowsExactlyAsync<IOException>(
                () => C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture)))!;
            if (!C1OutcomeInspectionOwnerPhase.IsAllowedBusyError(error))
            { throw new InvalidOperationException(UnexpectedNativeErrorMessage); }
            var native = (Win32Exception)error.InnerException!;
            await Assert.That(native.NativeErrorCode)
                .IsEqualTo(OperatingSystem.IsMacOS() ? EagainMac : EagainLinux);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task AcCrsDiag004OwnedLifetimePreservesActualNativeBodyFailure()
    {
        IOException? observed = null;
        var thrown = (await Assert.ThrowsExactlyAsync<IOException>(() =>
            C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
            {
                using var held = OfflineRegularFile.Open(fixture.OuterOwnerLockPath,
                    FileAccess.ReadWrite, FileShare.None, 1024);
                observed = (await Assert.ThrowsExactlyAsync<IOException>(
                    () => C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture)))!;
                if (!C1OutcomeInspectionOwnerPhase.IsAllowedBusyError(observed))
                { throw new InvalidOperationException(UnexpectedNativeErrorMessage); }
                throw observed;
            })))!;
        await Assert.That(observed).IsNotNull();
        await Assert.That(ReferenceEquals(thrown, observed)).IsTrue();
    }
}
