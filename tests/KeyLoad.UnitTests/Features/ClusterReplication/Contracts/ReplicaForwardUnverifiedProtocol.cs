namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUnverifiedProtocol
{
    internal const string Collection = "forward_unverified_docs";
    internal const string Writer = "forward-unverified-writer";
    internal const string OriginalDocument = "original";
    internal const string NextDocument = "next";
    internal const string OriginalJson = "{\"value\":1}";
    internal const string NextJson = "{\"value\":2}";
    internal const string Missing = "The genuine unverified forward observation is missing.";
    internal const string OwnedReplyRequired = "The actual signed reply must expose its owned payload buffer.";
    internal const string BoundExceeded = "The unverified forward test observation exceeds its bound.";
    internal const int MaximumObserved = 4;
    internal const int FirstByte = 0;
    internal const byte ChangedBit = 1;
    internal const int NoRecords = 0;
    internal const long NoBytes = 0;
    internal const int One = 1;
    internal const int ReplayCount = 2;
    internal const long NewRecord = 0;
    internal const long FirstRevision = 1;
}
