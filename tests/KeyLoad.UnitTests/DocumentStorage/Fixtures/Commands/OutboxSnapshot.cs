using System.Collections.Immutable;
namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed record OutboxSnapshot(byte[]? HeadBytes, ImmutableArray<byte[]> EntryBytes, long Tail);
