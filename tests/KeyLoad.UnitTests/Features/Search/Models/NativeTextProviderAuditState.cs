namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProviderAuditState
{
    internal List<NativeTextProviderAuditHistory> History { get; } = [];
    internal bool Updated
    {
        get; set;
    }
    internal bool Healthy
    {
        get; set;
    }
}
