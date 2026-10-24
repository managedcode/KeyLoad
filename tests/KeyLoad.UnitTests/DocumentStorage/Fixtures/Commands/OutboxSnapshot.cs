using System.Collections.Immutable;
namespace KeyLoad.UnitTests.DocumentStorage;

internal sealed record OutboxSnapshot(byte[]? HeadBytes, ImmutableArray<byte[]> EntryBytes, long Tail);
