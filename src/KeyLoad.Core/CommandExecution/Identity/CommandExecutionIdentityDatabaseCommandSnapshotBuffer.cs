namespace KeyLoad.Core;
/// <summary>Executes CommandExecutionIdentityDatabaseCommandSnapshotBuffer operations against the caller's database context.</summary>
public static class CommandExecutionIdentityDatabaseCommandSnapshotBuffer
{
    internal sealed class CommandSnapshotBuffer(int maximumBytes) : MemoryStream
    {
        private const int SingleByte = 1;
        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            RequireLength(Position + count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            RequireLength(Position + buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            RequireLength(Position + SingleByte);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            RequireLength(value);
            base.SetLength(value);
        }

        private void RequireLength(long value)
        {
            if (value > maximumBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AtomicCommandCommit.InvalidCommandBudgetMessage);
            }
        }
    }
}
