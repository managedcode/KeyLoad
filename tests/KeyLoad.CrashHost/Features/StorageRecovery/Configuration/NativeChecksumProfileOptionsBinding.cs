using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Binds and validates the exact child environment through the native options factory.</summary>
[ConfigurationBinding]
internal static class NativeChecksumProfileOptionsBinding
{
    internal static IOptions<NativeChecksumProfileOptions> Child()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var factory = new OptionsFactory<NativeChecksumProfileOptions>(
            [new ConfigureNamedOptions<NativeChecksumProfileOptions>(Options.DefaultName, selected =>
            {
                selected.DotnetIntrinsics = configuration[NativeChecksumProfileProtocol.DotnetIntrinsics];
                selected.ComPlusIntrinsics = configuration[NativeChecksumProfileProtocol.ComPlusIntrinsics];
            })], [], [new NativeChecksumProfileOptionsValidator()]);
        var options = new OptionsManager<NativeChecksumProfileOptions>(factory);
        _ = options.Value;
        return options;
    }
}
