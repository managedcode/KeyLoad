namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SafeProjectorOperationRf3Selection(string Id, string SqlSource, string Path,
    string MaskedJson, string RawJson, string? HiddenSourcePath)
{
    public override string ToString() => nameof(SafeProjectorOperationRf3Selection);
}
