namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Names only safe foreign Docker fields; credentials and environment values are never captured.</summary>
internal static class ReplicaIsolationNativeKeys
{
    internal const string Id = "Id";
    internal const string Name = "Name";
    internal const string Image = "Image";
    internal const string ConfigImage = "ConfigImage";
    internal const string User = "User";
    internal const string State = "State";
    internal const string StartedAt = "StartedAt";
    internal const string CapAdd = "CapAdd";
    internal const string Privileged = "Privileged";
    internal const string NetworkMode = "NetworkMode";
    internal const string PidMode = "PidMode";
    internal const string Networks = "Networks";
    internal const string NetworkId = "NetworkID";
    internal const string IpAddress = "IPAddress";
    internal const string Incarnation = "Incarnation";
    internal const string FaultSource = "FaultSource";
    internal const string Layers = "Layers";
    internal const string Revision = "Revision";
    internal const string Os = "Os";
    internal const string Architecture = "Architecture";
    internal const string Linux = "linux";
    internal const string Running = "running";
    internal const string NetAdmin = "NET_ADMIN";
    internal const string ImageInspect = "image";
    internal const string Inspect = "inspect";
    internal const string Format = "--format";
    internal const string ContainerFormat = """{"Id":{{json .Id}},"Name":{{json .Name}},"Image":{{json .Image}},"ConfigImage":{{json .Config.Image}},"User":{{json .Config.User}},"State":{{json .State.Status}},"StartedAt":{{json .State.StartedAt}},"CapAdd":{{json .HostConfig.CapAdd}},"Privileged":{{json .HostConfig.Privileged}},"NetworkMode":{{json .HostConfig.NetworkMode}},"PidMode":{{json .HostConfig.PidMode}},"Networks":{{json .NetworkSettings.Networks}},"Incarnation":{{json (index .Config.Labels "keyload.fault.incarnation")}},"FaultSource":{{json (index .Config.Labels "keyload.fault.source")}}}""";
    internal const string ImageFormat = """{"Id":{{json .Id}},"User":{{json .Config.User}},"Layers":{{json .RootFS.Layers}},"Revision":{{json (index .Config.Labels "org.opencontainers.image.revision")}},"FaultSource":{{json (index .Config.Labels "keyload.fault.source")}},"Os":{{json .Os}},"Architecture":{{json .Architecture}}}""";
}
