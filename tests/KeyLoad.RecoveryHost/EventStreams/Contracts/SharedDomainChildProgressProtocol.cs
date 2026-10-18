namespace KeyLoad.RecoveryHost.Features.EventStreams;

internal static class SharedDomainChildProgressProtocol
{
    internal const string FileName = "child-progress.bin";
    internal const string Alias = "keyload.test.shared-domain-child-progress.v1";
    internal const string PhaseAlias = "keyload.test.shared-domain-child-progress-phase.v1";
    internal const string Invalid = "The original shared-domain child progress witness is invalid.";
    internal const int Version = 1;
    internal const int MaximumBytes = 1024;
    internal const int InitialOrdinal = 0;
    internal const long EmptyPosition = 0L;
}
