namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageExpected(IReadOnlyDictionary<string, NativeCoverageImageExpectedFile> Files,
    string ToolClosureDigest);
