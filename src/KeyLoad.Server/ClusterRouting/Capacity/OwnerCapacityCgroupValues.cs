using System.Globalization;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OwnerCapacityCgroupValues
{
    private const char StatisticsLineDelimiter = '\n';
    private const int FirstField = 0;
    private const int SecondField = 1;
    private const int PairFields = 2;
    private const long MinimumLimit = 1;
    private const long MinimumUsage = 0;
    private const string UsageField = "usage_usec";
    private const string Unbounded = "max";
    private const char FieldSeparator = ' ';

    internal static bool Memory(string current, string limit, out long usage, out long maximum)
        => Integer(current, MinimumUsage, out usage) & Integer(limit, MinimumLimit, out maximum);

    internal static bool CpuLimit(string value, out long quota, out long period)
    {
        quota = MinimumUsage;
        period = MinimumUsage;
        var pair = value.Trim().Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries);
        return pair.Length == PairFields && Integer(pair[FirstField], MinimumLimit, out quota)
            && Integer(pair[SecondField], MinimumLimit, out period);
    }

    internal static bool CpuUsage(string value, out long usage)
    {
        usage = MinimumUsage;
        foreach (var line in value.Split(StatisticsLineDelimiter, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(FieldSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == PairFields && fields[FirstField] == UsageField)
            {
                return Integer(fields[SecondField], MinimumUsage, out usage);
            }
        }
        return false;
    }

    private static bool Integer(string value, long minimum, out long number)
    {
        number = MinimumUsage;
        return value.Trim() != Unbounded && long.TryParse(value.Trim(), NumberStyles.None,
            CultureInfo.InvariantCulture, out number) && number >= minimum;
    }
}
