using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>The actual two runtime environment selectors for this owned checksum child.</summary>
[ConfigurationOptions]
internal sealed class NativeChecksumProfileOptions
{
    public string? DotnetIntrinsics { get; set; }
    public string? ComPlusIntrinsics { get; set; }

    internal bool IsValid() => IsProfile(DotnetIntrinsics) && IsProfile(ComPlusIntrinsics)
        && DotnetIntrinsics == ComPlusIntrinsics;

    private static bool IsProfile(string? value)
        => value is NativeChecksumProfileProtocol.Enabled or NativeChecksumProfileProtocol.Disabled;
}

internal sealed class NativeChecksumProfileOptionsValidator : IValidateOptions<NativeChecksumProfileOptions>
{
    public ValidateOptionsResult Validate(string? name, NativeChecksumProfileOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(NativeChecksumProfileProtocol.Invalid);
}
