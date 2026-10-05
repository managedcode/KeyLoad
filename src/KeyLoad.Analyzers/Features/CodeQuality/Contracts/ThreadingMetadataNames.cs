namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class ThreadingMetadataNames
{
    public const string Lock = "System.Threading.Lock";
    public const string Monitor = "System.Threading.Monitor";
    public const string Enter = "Enter";
    public const string EnterScope = "EnterScope";
    public const string TryEnter = "TryEnter";
}
