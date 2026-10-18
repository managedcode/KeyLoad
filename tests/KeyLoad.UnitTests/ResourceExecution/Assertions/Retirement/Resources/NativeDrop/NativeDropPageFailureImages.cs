using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Allows only the actual native failed-outcome/clock/applied rows, conserving every other canonical byte.</summary>
internal static class NativeDropPageFailureImages
{
    internal static async Task RequireAsync(NativeDropPageAttemptState state, Command<DatabaseCommand> original,
        NativeDropPageRecoveryImage[] before, ErrorCode expected)
    {
        var after = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        var outcomeKey = KeySpace.GlobalOutcome(original.UserId!, original.CommandId);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            Convert.ToHexString(outcomeKey), Convert.ToHexString(KeySpace.ClockBytes), Convert.ToHexString(KeySpace.AppliedBytes)
        };
        for (var index = default(int); index < after.Length; index++)
        {
            var node = state.Profile.Hosts.Nodes[index];
            var receiver = node.Store.Identity.Incarnation == state.Receiver.Owner.Incarnation;
            var oldRows = receiver ? Filter(before[index].Rows, allowed) : before[index].Rows;
            var newRows = receiver ? Filter(after[index].Rows, allowed) : after[index].Rows;
            await NativeDropRuntimeAssertions.SameAsync(oldRows, newRows).ConfigureAwait(false);
            if (receiver)
            {
                var outcome = node.Store.Read(view => view.GetRecord<StoredOutcome>(outcomeKey));
                await Assert.That(Errors.Code(outcome!.Result.Problem)).IsEqualTo(expected);
                await Assert.That(outcome.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(original));
                await Assert.That(outcome.Result.Value).IsNull();

                await Assert.That(after[index].LastIndex).IsEqualTo(before[index].LastIndex + NativeDropRuntimeProtocol.FirstPage);
                await Assert.That(after[index].CommittedIndex).IsEqualTo(after[index].LastIndex);
            }
            else
            {
                await Assert.That(after[index].Position).IsEqualTo(before[index].Position);
                await Assert.That(after[index].LastIndex).IsEqualTo(before[index].LastIndex);
                await Assert.That(after[index].CommittedIndex).IsEqualTo(before[index].CommittedIndex);
            }
        }
    }

    private static string[] Filter(string[] rows, HashSet<string> excluded)
        => rows.Where(row => !excluded.Contains(row[..row.IndexOf(':', StringComparison.Ordinal)])).ToArray();
}
