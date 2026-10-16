using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Observed original healthy voter identities and their actual Docker bind sources.</summary>
internal sealed record FunctionalGatePhysicalFileEvidence(FunctionalGatePhysicalFileOwner[] Owners);

internal sealed record FunctionalGatePhysicalFileOwner(string Resource, string Container, string Directory,
    Guid NodeId, Guid Incarnation, ContainerRuntimeInspection Runtime);

internal static class FunctionalGatePhysicalFileProtocol
{
    internal const string Inspect = "inspect";
    internal const string Format = "--format";
    internal const string MountFormat = "{\"Mounts\":{{json .Mounts}},\"Running\":{{.State.Running}},\"DataDirectory\":\"{{range .Config.Env}}{{if eq . \"KeyLoad__DataDirectory=/data\"}}/data{{end}}{{end}}\"}";
    internal const string Mounts = "Mounts";
    internal const string Running = "Running";
    internal const string DataDirectory = "DataDirectory";
    internal const string Destination = "Destination";
    internal const string Source = "Source";
    internal const string Type = "Type";
    internal const string Writable = "RW";
    internal const string Bind = "bind";
    internal const string ContainerDirectory = "/data";
    internal const string Canonical = "database";
    internal const string Replica = "replica";
    internal const string Tree = "tree";
    internal const string Identity = "identity.json";
    internal const string Failure = "The original functional voter physical file evidence is invalid.";
    internal const int MaximumEntries = 4096;
    internal const int MaximumPathCharacters = 4096;
    internal const int MaximumIdentityBytes = 65536;
    internal const int StreamBufferBytes = 4096;
    internal const int NoEntries = 0;
    internal const int RequiredDataMounts = 1;
}
