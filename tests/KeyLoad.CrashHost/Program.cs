using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

var directory = args[0];
var stage = Enum.Parse<CommitStage>(args[1]);
var mutationIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
using var store = new ZoneTreeStore(new(directory)
{
    FaultObserver = (observed, position, index) =>
    {
        if (position == 2 && observed == stage && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            Console.WriteLine("crash-point"); Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
    }
});
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 0); return true; });
store.Commit((tx, _) => { for (var i = 0; i < 3; i++) tx.PutRecord(KeyCodec.Encode("item", (long)i), 1); return true; });
Console.WriteLine("ack"); Console.Out.Flush();
Thread.Sleep(Timeout.Infinite);
public sealed class CrashHostMarker;
