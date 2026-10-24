using KeyLoad.Storage;

namespace KeyLoad.UnitTests.EventStreams;

internal sealed record SharedDomainCut(StoreIdentity Identity, long Position, string[] Rows);
