namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeSimdSelectionProgram
{
    internal const string Source = """
        for (const option of ['DOTNET_EnableHWIntrinsic', 'COMPlus_EnableHWIntrinsic',
          'DOTNET_EnableSIMD', 'COMPlus_EnableSIMD']) {
          let refused = false;
          try { nativeSelection(['--KeyLoadTests:Suite=unit'], { [option]: '0' }); }
          catch (failure) {
            if (failure.message !== 'Native SIMD must remain enabled.') throw failure;
            refused = true;
          }
          if (!refused) throw new Error('Disabled native SIMD selection was accepted.');
          const enabled = nativeSelection(['--KeyLoadTests:Suite=unit'], { [option]: '1' });
          if (enabled.environment[option] !== '1'
            || enabled.args[enabled.args.indexOf('--maximum-parallel-tests') + 1] !== '50') {
            throw new Error('Enabled native SIMD selection lost its environment or native admission.');
          }
        }
        """;
}
