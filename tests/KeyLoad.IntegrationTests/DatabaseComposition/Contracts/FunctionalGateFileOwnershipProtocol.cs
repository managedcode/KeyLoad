namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Finite evidence bounds for the explicit native same-live-files refusal scenario.</summary>
internal static class FunctionalGateFileOwnershipProtocol
{
    internal const int Voters = 3;
    internal const int FirstContender = 0;
    internal const int SuccessExit = 0;
    internal const int MaximumLogCharacters = 16_384;
    internal const int ContainerIdCharacters = 64;
    internal const string DataDirectory = "/data";
    internal const string Inspect = "inspect";
    internal const string Format = "--format";
    internal const string Inspection = "{\"id\":{{json .Id}},\"image\":{{json .Image}},\"configured\":{{json .Config.Image}},\"state\":{{json .State.Status}},\"exit\":{{.State.ExitCode}},\"mounts\":{{json .Mounts}}}";
    internal const string Running = "running";
    internal const string Exited = "exited";
    internal const string Bind = "bind";
    internal const string IoFailure = "System.IO.IOException";
    internal const string LockPath = "/data/node.owner.lock";
    internal const string OriginalOwner = "PartitionStores";
    internal const string Redacted = "[redacted]";
    internal const string LogEvidence = "original-log";
    internal const string RuntimeEvidence = "actual-runtime";
    internal const string EvidenceDirectory = "file-ownership-refusals";
    internal const string IdProperty = "id";
    internal const string ImageProperty = "image";
    internal const string ConfiguredProperty = "configured";
    internal const string StateProperty = "state";
    internal const string ExitProperty = "exit";
    internal const string MountsProperty = "mounts";
    internal const string DestinationProperty = "Destination";
    internal const string TypeProperty = "Type";
    internal const string WritableProperty = "RW";
    internal const string SourceProperty = "Source";
    internal const string LogLineEnding = "\n";
    internal const char LogLineEndingCharacter = '\n';
    internal const string LogEvidenceKey = "KeyLoad.FileOwnership.OriginalLogPrefix";
    internal const string Incomplete = "The original native file-ownership refusal evidence is incomplete.";
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);
}
