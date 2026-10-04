using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal readonly record struct ReplicaReplayAdmissionFailure(int SenderIndex, ReplicaReplayPool Pool, ReplicaRpc Method,
    int CriticalCount, int ForwardCount, int ReadCount, int DataCount, int Capacity, int VoterMaximum,
    int NodeMaximum, long ObservedUnixMilliseconds, long OldestExpiryUnixMilliseconds);
