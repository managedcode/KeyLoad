namespace KeyLoad.ProcessRecoveryTests.DocumentStorage;

internal sealed record CompositePhysicalIndexImage(string PartitionKey, string Space, string IndexName,
    string[] KeyHex, string[] DocumentIds, string[] AfterFourIds);
