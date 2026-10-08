namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Bounds foreground native namespace fault control without changing request or startup deadlines.</summary>
internal static class ReplicaIsolationProtocol
{
    internal const int PeerCount = 2;
    internal const int FullContainerIdLength = 64;
    internal const int ChainSuffixLength = 20;
    internal const string Input = "INPUT";
    internal const string Output = "OUTPUT";
    internal const string InputPrefix = "KL21I_";
    internal const string OutputPrefix = "KL21O_";
    internal const string Create = "-N";
    internal const string Append = "-A";
    internal const string Insert = "-I";
    internal const string Delete = "-D";
    internal const string Flush = "-F";
    internal const string DeleteChain = "-X";
    internal const string Jump = "-j";
    internal const string Drop = "DROP";
    internal const string SourcePort = "--sport";
    internal const string DestinationPort = "--dport";
    internal const string SiloPort = "11111";
    internal const string Source = "-s";
    internal const string Destination = "-d";
    internal const string Protocol = "-p";
    internal const string Tcp = "tcp";
    internal const string Exec = "exec";
    internal const string User = "--user";
    internal const string RootUser = "0";
    internal const string Timeout = "/usr/bin/timeout";
    internal const string TermSignal = "--signal=TERM";
    internal const string KillBound = "--kill-after=1s";
    internal const string ExecutionBound = "5s";
    internal const string Iptables = "/usr/sbin/iptables";
    internal const string Wait = "-w";
    internal const string WaitBound = "4";
    internal const string FirstRule = "1";
}
