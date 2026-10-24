namespace KeyLoad.UnitTests.TestInfrastructure;

internal static class SimdSelectionProgram
{
    internal const string Source = """
        for (const option of ['DOTNET_EnableHWIntrinsic', 'COMPlus_EnableHWIntrinsic',
          'DOTNET_EnableSIMD', 'COMPlus_EnableSIMD']) {
          let refused = false;
          try { runtimeSelection(['--KeyLoadTests:Suite=unit'], { [option]: '0' }); }
          catch (failure) {
            if (failure.message !== 'Execution SIMD must remain enabled.') throw failure;
            refused = true;
          }
          if (!refused) throw new Error('Disabled execution SIMD selection was accepted.');
          const enabled = runtimeSelection(['--KeyLoadTests:Suite=unit'], { [option]: '1' });
          if (enabled.environment[option] !== '1'
            || enabled.args[enabled.args.indexOf('--maximum-parallel-tests') + 1] !== '50') {
            throw new Error('Enabled execution SIMD selection lost its environment or execution admission.');
          }
        }
        """;
}
