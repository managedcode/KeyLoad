namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Frozen native Microsoft coverage output and selection tokens.</summary>
internal static class NativeCoverageProtocol
{
    internal const string CoberturaFormat = "cobertura";
    internal const string BinaryFormat = "coverage";
    internal const string FormatSetting = "KeyLoadTests:CoverageFormat";
    internal const string InvalidSelection = "The native coverage format selection is invalid.";
}
