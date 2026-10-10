namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeCapabilitySelectionProgram
{
    internal const string Declaration = """

            const capabilityArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay", localArgs[2]];
            const capability = nativeSelection(capabilityArgs, {});
            let capabilityUnsupportedCount = 0;
            for (const badFilter of ['/*/*/NativeCapabilityOmissionRf3Tests/*',
              '/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcp*',
              '/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay|/*/*/OtherCase/*', '/*/*/NativeCapabilityOmissionRf3Tests/ForegroundSdkOrOfficialMcpOmittedCapabilityRefusesThenSameOwnerRepairAndColdReplay ']) {
              try { nativeSelection([capabilityArgs[0], '--KeyLoadTests:Filter='+badFilter, capabilityArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                capabilityUnsupportedCount++; continue;
              }
              throw new Error('Unsupported native capability RF3 filter was accepted.');
            }

            """;
}
