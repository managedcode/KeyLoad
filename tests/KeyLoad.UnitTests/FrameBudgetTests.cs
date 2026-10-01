using System.Buffers.Binary;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

public sealed class FrameBudgetTests
{
    [Fact]
    public void IncrementalFrameAccountingMatchesCanonicalJsonAtExactBase64Boundaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-frame-accounting-" + Guid.NewGuid().ToString("N")); var random = new Random(60731);
        try
        {
            for (var trial = 0; trial < 12; trial++)
            {
                var first = new byte[trial * 17]; random.NextBytes(first);
                var last = new byte[trial * 19 + 5]; random.NextBytes(last);
                StorageMutation[] final = [new([0xFB, 0xFF], last), new([0xFF, 0xFB], null)];
                var payload = JsonDefaults.Serialize(final); var directory = Path.Combine(root, trial.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using var store = new ZoneTreeStore(new(directory) { MaxFrameBytes = payload.Length });
                store.Commit((tx, _) =>
                {
                    tx.Put(final[0].Key, first); tx.ValidateCommit();
                    tx.Put(final[0].Key, last); tx.Delete(final[1].Key); tx.ValidateCommit(); return true;
                });
                byte[] journal;
                using (var file = new FileStream(Path.Combine(directory, "commands.wal"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                { journal = new byte[file.Length]; file.ReadExactly(journal); }
                Assert.Equal(payload.Length, BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(8)));
                Assert.Equal(payload, journal.AsSpan(52).ToArray());
                Assert.Equal(last, store.Read(view => view.Get(final[0].Key)));
                Assert.Equal(ErrorCode.ResourceExhausted, Assert.Throws<KeyLoadException>(() => store.Commit((tx, _) =>
                { tx.Put(final[0].Key, new byte[payload.Length]); return true; })).Code);
                Assert.Equal(1, store.Position); Assert.Equal(last, store.Read(view => view.Get(final[0].Key)));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
