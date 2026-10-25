namespace KeyLoad.FunctionalTests.CodeQuality;

/// <summary>Frozen execution Microsoft coverage output and selection tokens.</summary>
internal static class CoverageProtocol
{
    internal const string CoberturaFormat = "cobertura";
    internal const string BinaryFormat = "coverage";
    internal const string FormatSetting = "KeyLoadTests:CoverageFormat";
    internal const string InvalidSelection = "The execution coverage format selection is invalid.";
    internal const string ToolVersionMetadataKey = "KeyLoadCoverageToolVersion";
    internal const string ToolPackageRootMetadataKey = "KeyLoadCoveragePackageRoot";
    internal const string InvalidToolMetadata = "The execution coverage package metadata is absent or inconsistent.";
    internal const int MaximumDescriptorFormatBytes = 1_048_576;
    internal const int MaximumPathFormatCharacters = 4_096;
}
