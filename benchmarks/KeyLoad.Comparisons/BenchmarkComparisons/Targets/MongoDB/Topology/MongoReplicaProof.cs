using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal sealed record MongoReplicaProof(ClusterEvidence Evidence, string Version, IMongoClient[] SecondaryClients);
