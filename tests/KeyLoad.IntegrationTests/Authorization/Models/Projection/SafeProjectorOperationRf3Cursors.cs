namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SafeProjectorOperationRf3Cursors(string SdkQuery, string McpQuery, string Feed)
{
    public override string ToString() => nameof(SafeProjectorOperationRf3Cursors);
}
