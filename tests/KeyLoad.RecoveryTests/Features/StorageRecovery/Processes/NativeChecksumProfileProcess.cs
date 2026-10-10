using System.Diagnostics;
using KeyLoad.CrashHost;
using KeyLoad.RecoveryTests.Features.DocumentStorage;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NativeChecksumProfileProcess
{
    private const string Prefix = "keyload-native-checksum-profile-";
    private const string GuidFormat = "N";
    private const string Dotnet = "dotnet";
    private const string Failed = "The current native checksum profile child failed.";

    internal static async Task RunAsync(bool firstScalar, CancellationToken callerToken)
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        using var originalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, originalTimeout.Token);
        CommandIdempotencyProcessChild? active = null;
        Exception? primary = null;
        try
        {
            foreach (var (phase, scalar) in new[] { (NativeChecksumProfileProtocol.Seed, firstScalar),
                (NativeChecksumProfileProtocol.Extend, !firstScalar), (NativeChecksumProfileProtocol.Cold, firstScalar) })
            {
                active = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters,
                    NativeChecksumProfileProtocol.CompletionSignalPrefix + phase + ":"
                        + (scalar ? NativeChecksumProfileProtocol.Disabled : NativeChecksumProfileProtocol.Enabled));
                active.Start(Start(root, phase, scalar));
                active.ThrowStartupFailure();
                await active.WaitAndJoinAsync(timeout.Token);
                if (active.ExitCode != NativeChecksumProfileProtocol.Success)
                { throw new InvalidOperationException(Failed); }
                await KilledProcessFileReadiness.WaitAsync(root, timeout.Token);
                RecoveryFileInventory.AssertNativeHandlesReleased(root);
                active.CloseNativeProcessAfterJoin();
                active = null;
            }
        }
        catch (Exception error) when (CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        catch (Exception error) when (!CommandIdempotencyProcessFailureHandling.IsNonFatal(error)) { primary = error; }
        await CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(root, active, primary);
    }

    private static ProcessStartInfo Start(string root, string phase, bool scalar)
    {
        var start = new ProcessStartInfo(Dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var value in new[] { typeof(CrashHostMarker).Assembly.Location, NativeChecksumProfileProtocol.Mode, root, phase,
            scalar ? NativeChecksumProfileProtocol.Disabled : NativeChecksumProfileProtocol.Enabled })
        { start.ArgumentList.Add(value); }
        var profile = scalar ? NativeChecksumProfileProtocol.Disabled : NativeChecksumProfileProtocol.Enabled;
        start.Environment[NativeChecksumProfileProtocol.DotnetIntrinsics] = profile;
        start.Environment[NativeChecksumProfileProtocol.ComPlusIntrinsics] = profile;
        return start;
    }
}
