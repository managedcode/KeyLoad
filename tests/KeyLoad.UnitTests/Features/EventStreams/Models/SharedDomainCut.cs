using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed record SharedDomainCut(StoreIdentity Identity, long Position, string[] Rows);
