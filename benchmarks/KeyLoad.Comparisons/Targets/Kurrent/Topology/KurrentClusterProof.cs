using KurrentDB.Client;

namespace KeyLoad.Comparisons.Targets;

internal sealed record KurrentClusterProof(KurrentDBClient[] NodeClients);
