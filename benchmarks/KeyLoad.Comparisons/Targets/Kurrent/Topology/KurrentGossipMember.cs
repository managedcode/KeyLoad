namespace KeyLoad.Comparisons.Targets;

internal sealed record KurrentGossipMember(string Id, string State, string Version, string HttpEndpointIp,
    int HttpEndpointPort, string InternalHttpEndpointIp, int InternalHttpEndpointPort, bool IsAlive,
    bool IsReadOnly, long Commit, long Writer, long Chaser);
