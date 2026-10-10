namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativePurposeSelectionProgram
{
    internal const string Declaration = """
            const purposeArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts", localArgs[2]];
            const purpose = nativeSelection(purposeArgs, {});
            let purposeUnsupportedCount = 0;
            for (const badFilter of ["/*/*/NativeSignedPurposeRf3Tests/*",
              "/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurpose*",
              "/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts|/*/*/OtherCase/*", "/*/*/NativeSignedPurposeRf3Tests/ForegroundSdkOrOfficialMcpSignedPurposeRefusesThenExactRepairReplayAndTwoColdCuts "]) {
              try { nativeSelection([purposeArgs[0], '--KeyLoadTests:Filter='+badFilter, purposeArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                purposeUnsupportedCount++; continue;
              }
              throw new Error('Unsupported signed purpose RF3 filter was accepted.');
            }
            """;
}
