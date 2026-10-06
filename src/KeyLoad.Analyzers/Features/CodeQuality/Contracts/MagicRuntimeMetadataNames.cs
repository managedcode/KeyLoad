namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class MagicRuntimeMetadataNames
{
    internal const string FriendAssemblyAttribute = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";
    internal const string TimeSpan = "System.TimeSpan";
    internal const string Task = "System.Threading.Tasks.Task";
    internal const string GenericTask = "System.Threading.Tasks.Task`1";
    internal const string Semaphore = "System.Threading.SemaphoreSlim";
    internal const string CancellationSource = "System.Threading.CancellationTokenSource";
    internal const string FromPrefix = "From";
    internal const string Delay = "Delay";
    internal const string Wait = "Wait";
    internal const string WaitAsync = "WaitAsync";
    internal const string CancelAfter = "CancelAfter";
}
