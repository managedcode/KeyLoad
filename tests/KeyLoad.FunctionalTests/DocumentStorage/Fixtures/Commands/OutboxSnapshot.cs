using System.Collections.Immutable;
namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed record OutboxSnapshot(byte[]? HeadBytes, ImmutableArray<byte[]> EntryBytes, long Tail);
