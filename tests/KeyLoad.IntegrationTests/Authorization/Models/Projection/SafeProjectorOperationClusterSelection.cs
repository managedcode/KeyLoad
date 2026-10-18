namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SafeProjectorOperationClusterSelection(string Id, string SqlSource, string Path,
    string MaskedJson, string RawJson, string? HiddenSourcePath)
{
    public override string ToString() => nameof(SafeProjectorOperationClusterSelection);
}
