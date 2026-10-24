namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartitionStorageProtocol
{
    internal const string Root = "root";
    internal const string PartitionKey = "partition-a";
    internal const string Collection = "execution-storage-lifetime";
    internal const string Document = "original";
    internal const string Json = "{\"text\":\"execution storage\"}";
    internal const string PutKind = "putDocument";
    internal const long InitialRevision = 1;
    internal const long CanonicalStep = 1;
    internal const int PersistedCopies = 1;
    internal static readonly PartitionRef Partition = new("system", "execution-storage", "knowledge", PartitionKey);
    internal static readonly Guid Configure = Guid.Parse("f955a135-81fb-4b15-acf7-ab7f32732c07");
    internal static readonly Guid Command = Guid.Parse("b4cb2998-d3bb-413c-924d-21c67cb1fe4f");
    internal static CommandRequest Request() => new(Command, Partition,
        [new PutDocument(Collection, Document, Json)]);
    internal static EntityRef Reference() => new(Partition, Collection, Document);
}
