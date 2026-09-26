using System.Globalization;
using System.Text.RegularExpressions;

namespace EricoreTech.CandleSharp.Domain
{
    /// <summary>
    /// One parsed rule condition. The text syntax (case-insensitive):
    /// <code>
    ///   RSI_14 &lt; 30                     compare a value with a number: &lt; &lt;= &gt; &gt;=
    ///   Close &gt; SMA_50                  ...or with another value
    ///   Close crosses above SMA_50       true only on the bar the line is crossed (also: crosses below)
    ///   SMA_50 vs close &lt; -2%           a price level as distance from the close
    ///   MACD_12_26_9 is Bullish          an indicator's stance (Bullish, Bearish, Neutral)
    ///   MACD_12_26_9 turns Bullish       only on the bar the stance flips
    /// </code>
    /// Values are Open, High, Low, Close, Volume, or any indicator column.
    /// </summary>
    public sealed partial class RuleCondition
    {
        private enum Op { Less, LessEq, Greater, GreaterEq, CrossAbove, CrossBelow, Is, Turns }

        private sealed record Operand(string Name, bool VsClose, double? Constant);

        private readonly Op _op;
        private readonly Operand _left;
        private readonly Operand? _right;
        private readonly SignalDirection _direction;

        private RuleCondition(string text, Op op, Operand left, Operand? right, SignalDirection direction)
        {
            Text = text;
            _op = op;
            _left = left;
            _right = right;
            _direction = direction;
        }

        public string Text { get; }

        /// <summary>The indicator column or stance names this condition reads.</summary>
        public IEnumerable<string> ReferencedNames =>
            new[] { _left, _right }.Where(o => o is { Constant: null }).Select(o => o!.Name);

        public bool IsStanceCondition => _op is Op.Is or Op.Turns;

        public static RuleCondition Parse(string text)
        {
            var trimmed = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (trimmed.Length == 0) throw new FormatException("empty condition");

            var stance = StancePattern().Match(trimmed);
            if (stance.Success)
            {
                var direction = Enum.Parse<SignalDirection>(stance.Groups["dir"].Value, ignoreCase: true);
                bool turns = stance.Groups["op"].Value.Equals("turns", StringComparison.OrdinalIgnoreCase);
                if (turns && direction == SignalDirection.Neutral)
                    throw new FormatException($"\"{trimmed}\": use \"turns Bullish\" or \"turns Bearish\"");
                return new RuleCondition(trimmed, turns ? Op.Turns : Op.Is,
                    new Operand(stance.Groups["name"].Value, false, null), null, direction);
            }

            // Symbol operators may be written without spaces: "RSI_14<30".
            var spaced = Regex.Replace(trimmed, @"\s*(<=|>=|<|>)\s*", " $1 ").Trim();
            var compare = ComparePattern().Match(spaced);
            if (!compare.Success)
                throw new FormatException(
                    $"can't read \"{trimmed}\". Examples: \"RSI_14 < 30\", \"Close crosses above SMA_50\", \"MACD_12_26_9 is Bullish\"");
            var op = compare.Groups["op"].Value.ToLowerInvariant() switch
            {
                "<" => Op.Less,
                "<=" => Op.LessEq,
                ">" => Op.Greater,
                ">=" => Op.GreaterEq,
                "crosses above" => Op.CrossAbove,
                _ => Op.CrossBelow,
            };
            var left = ParseOperand(compare.Groups["left"].Value, trimmed);
            if (left.Constant is not null)
                throw new FormatException($"\"{trimmed}\": put the indicator on the left, e.g. \"RSI_14 < 30\"");
            return new RuleCondition(spaced, op, left, ParseOperand(compare.Groups["right"].Value, trimmed), default);
        }

        private static Operand ParseOperand(string text, string condition)
        {
            text = text.Trim();
            bool percent = text.EndsWith('%');
            if (double.TryParse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return new Operand(text, false, percent ? number / 100 : number);

            var match = OperandPattern().Match(text);
            if (!match.Success)
                throw new FormatException($"\"{condition}\": \"{text}\" is not a number or an indicator name");
            return new Operand(match.Groups["name"].Value, match.Groups["vs"].Success, null);
        }

        /// <summary>
        /// Evaluates the condition on every bar. Warm-up bars (a value not yet
        /// available) are false. Throws when a name isn't in the data.
        /// </summary>
        public bool[] Evaluate(RuleData data)
        {
            int n = data.Count;
            var result = new bool[n];
            if (IsStanceCondition)
            {
                var stance = data.Stance(_left.Name);
                for (int i = 0; i < n; i++)
                    result[i] = _op == Op.Is
                        ? stance[i] == _direction
                        : i > 0 && stance[i] == _direction && stance[i - 1] != _direction;
                return result;
            }

            var left = Series(data, _left);
            var right = Series(data, _right!);
            for (int i = 0; i < n; i++)
            {
                if (left[i] is not { } a || right[i] is not { } b) continue;
                result[i] = _op switch
                {
                    Op.Less => a < b,
                    Op.LessEq => a <= b,
                    Op.Greater => a > b,
                    Op.GreaterEq => a >= b,
                    _ => i > 0 && left[i - 1] is { } pa && right[i - 1] is { } pb
                        && (_op == Op.CrossAbove ? pa <= pb && a > b : pa >= pb && a < b),
                };
            }
            return result;
        }

        private static double?[] Series(RuleData data, Operand operand)
        {
            if (operand.Constant is { } c) return Enumerable.Repeat<double?>(c, data.Count).ToArray();
            var values = data.Values(operand.Name);
            if (!operand.VsClose) return values;
            var close = data.Values("Close");
            return values.Select((v, i) => v is { } x && close[i] is > 0 ? x / close[i]!.Value - 1 : (double?)null).ToArray();
        }

        public override string ToString() => Text;

        [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.\-]+) (?<op>is|turns) (?<dir>bullish|bearish|neutral)$", RegexOptions.IgnoreCase)]
        private static partial Regex StancePattern();

        [GeneratedRegex(@"^(?<left>.+?) (?<op><=|>=|<|>|crosses above|crosses below) (?<right>.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex ComparePattern();

        [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.\-]+)(?<vs> vs close)?$", RegexOptions.IgnoreCase)]
        private static partial Regex OperandPattern();
    }
}
