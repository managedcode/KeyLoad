using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed record SharedDomainCut(StoreIdentity Identity, long Position, string[] Rows);
