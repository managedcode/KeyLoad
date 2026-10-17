namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record SafeProjectorOperationSelection(string Id, string SqlSource, string Path,
    string MaskedJson, string RawJson, string? HiddenSourcePath)
{
    public override string ToString() => nameof(SafeProjectorOperationSelection);
}
