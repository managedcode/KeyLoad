namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record SafeProjectorOperationClusterCursors(string SdkQuery, string McpQuery, string Feed)
{
    public override string ToString() => nameof(SafeProjectorOperationClusterCursors);
}
