using System.Globalization;
using EricoreTech.CandleSharp.Domain;

namespace EricoreTech.CandleSharp.Agents
{
    internal static class SeriesLookup
    {
        /// <summary>First engine column whose name starts with the prefix, or null if that indicator isn't loaded.</summary>
        public static double?[]? Column(AgentInput input, string prefix)
        {
            foreach (var (name, values) in input.Signals.Columns)
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return values;
            return null;
        }

        public static double? LastValue(double?[]? column)
        {
            if (column is null) return null;
            for (int i = column.Length - 1; i >= 0; i--)
                if (column[i] is { } v) return v;
            return null;
        }

        /// <summary>Percentile rank (0-100) of value within the column's non-null history.</summary>
        public static double Percentile(IEnumerable<double> history, double value)
        {
            int below = 0, total = 0;
            foreach (var v in history)
            {
                total++;
                if (v <= value) below++;
            }
            return total == 0 ? 50 : 100.0 * below / total;
        }
    }
}
