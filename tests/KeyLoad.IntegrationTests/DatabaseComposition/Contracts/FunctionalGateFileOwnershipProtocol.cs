namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Finite evidence bounds for the actual execution same-live-files refusal scenario.</summary>
internal static class FunctionalGateFileOwnershipProtocol
{
    internal const int SiloCount = 3;
    internal const int FirstContender = 0;
    internal const int SuccessExit = 0;
    internal const int MaximumLogCharacters = 16_384;
    internal const string IoFailure = "System.IO.IOException";
    internal const string LockPath = "node.owner.lock";
    internal const string OriginalOwner = "PartitionStores";
    internal const string Redacted = "[redacted]";
    internal const string LogEvidence = "original-log";
    internal const string RuntimeEvidence = "actual-runtime";
    internal const string EvidenceDirectory = "file-ownership-refusals";
    internal const string LogLineEnding = "\n";
    internal const char LogLineEndingCharacter = '\n';
    internal const string LogEvidenceKey = "KeyLoad.FileOwnership.OriginalLogPrefix";
    internal const string Incomplete = "The original execution file-ownership refusal evidence is incomplete.";
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);
}
