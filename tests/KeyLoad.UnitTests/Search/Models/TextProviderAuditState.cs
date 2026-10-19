namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextProviderAuditState
{
    internal List<TextProviderAuditHistory> History { get; } = [];
    internal bool Updated
    {
        get; set;
    }
    internal bool Healthy
    {
        get; set;
    }
}
