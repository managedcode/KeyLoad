using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextResourceFileAccounting
{
    private const long AbsentFileLength = 0;
    private const long EmptyFileLength = 0;
    private const long NoAdditionalBytes = 0;
    internal static void Check(Dictionary<object, NativeTextResourceFileReservation> reservations,
        ref int files, ref long bytes, IOptions<NativeTextExecutionOptions> options)
    {
        foreach (var reservation in reservations.Values)
        {
            if (reservation.Key is NativeTextOpenFileGroup)
            { continue; }
            var exists = File.Exists(reservation.Path);
            if (exists)
            { NativeTextFileIO.VerifyRegularFile(reservation.Path); }
            var physicalLength = exists ? new FileInfo(reservation.Path).Length : AbsentFileLength;
            var observedLength = reservation.ObserveLength();
            if (observedLength < EmptyFileLength || reservation.TargetLength < EmptyFileLength)
            { throw NativeTextErrors.Corrupt(); }
            var projected = Math.Max(observedLength, reservation.TargetLength);
            var additional = Math.Max(NoAdditionalBytes, projected - physicalLength);
            if ((!exists && files >= options.Value.MaximumFiles)
                || additional > options.Value.MaximumDiskBytes - bytes)
            { throw NativeTextErrors.BoundExceeded(); }
            if (!exists)
            { files++; }
            bytes += additional;
        }
    }
}
