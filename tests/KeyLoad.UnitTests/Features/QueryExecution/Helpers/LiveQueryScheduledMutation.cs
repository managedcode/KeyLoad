using System.Globalization;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class LiveQueryScheduledMutation
{
    internal static LiveQueryTests.LiveQueryExpectation Apply(TestDatabase db, int index, int trial, int round,
        long[] revisions, bool[] deleted)
    {
        var id = "order-" + index.ToString(CultureInfo.InvariantCulture);
        CommitReceipt receipt;
        if (round == LiveQueryTestSchedule.DeleteRound)
        {
            receipt = db.Commit(new DeleteDocument("orders", id, revisions[index]));
            deleted[index] = true;
        }
        else
        {
            var status = round is LiveQueryTestSchedule.LeaveRound or LiveQueryTestSchedule.NonmatchingUpdateRound or LiveQueryTestSchedule.NonmatchingInsertRound
                ? "closed"
                : "open";
            var json = JsonSerializer.Serialize(new LiveQueryTests.LiveQueryOrder(trial, status), JsonDefaults.Options);
            receipt = db.Commit(new PutDocument("orders", id, json, revisions[index], ExplicitReplacement: true));
            deleted[index] = false;
        }

        revisions[index]++;
        return new(round is not LiveQueryTestSchedule.NonmatchingUpdateRound and not LiveQueryTestSchedule.NonmatchingInsertRound,
            round is LiveQueryTestSchedule.LeaveRound or LiveQueryTestSchedule.DeleteRound
                ? LiveQueryChangeKind.Remove
                : LiveQueryChangeKind.Upsert, receipt);
    }
}
