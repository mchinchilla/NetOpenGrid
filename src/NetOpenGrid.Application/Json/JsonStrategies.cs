using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Json;

/// <summary>
/// Reflection-free strategies over <see cref="JsonElement"/> rows.
/// Missing properties behave as null; kinds are compared with a deterministic rank.
/// </summary>
internal static class JsonStrategies
{
    public const int RankNull = 0;
    public const int RankBoolean = 1;
    public const int RankNumber = 2;
    public const int RankString = 3;

    internal static bool TryGetProperty(in JsonElement element, string field, out JsonElement value)
    {
        if (element.ValueKind is JsonValueKind.Object && element.TryGetProperty(field, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    internal static int KindRank(in JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => RankNull,
        JsonValueKind.True or JsonValueKind.False => RankBoolean,
        JsonValueKind.Number => RankNumber,
        JsonValueKind.String => RankString,
        _ => RankString + 1
    };

    internal static string? Format(in JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText()
    };

    internal sealed class SortStrategy(string field) : ISortStrategy<JsonElement>
    {
        private static readonly StringComparer TextComparer = StringComparer.Ordinal;

        public int Compare(JsonElement x, JsonElement y)
        {
            var hasX = TryGetProperty(x, field, out var vx);
            var hasY = TryGetProperty(y, field, out var vy);

            var rankX = hasX ? KindRank(vx) : RankNull;
            var rankY = hasY ? KindRank(vy) : RankNull;

            if (rankX != rankY)
            {
                return rankX.CompareTo(rankY);
            }

            return rankX switch
            {
                RankNull => 0,
                RankBoolean => vx.GetBoolean().CompareTo(vy.GetBoolean()),
                RankNumber => vx.GetDecimal().CompareTo(vy.GetDecimal()),
                _ => TextComparer.Compare(vx.ToString(), vy.ToString())
            };
        }
    }

    internal sealed class SearchStrategy(string field) : ISearchStrategy<JsonElement>
    {
        public bool Matches(JsonElement item, string term) =>
            TryGetProperty(item, field, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            value.GetString()?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    internal sealed class FilterStrategyFactory(string field, FilterOpSet supportedOps) : IFilterStrategyFactory<JsonElement>
    {
        public FilterOpSet SupportedOps { get; } = supportedOps;

        public IFilterStrategy<JsonElement>? Create(FilterOperator op, string? rawValue)
        {
            if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
            {
                return null;
            }

            var value = rawValue ?? string.Empty;

            return op switch
            {
                FilterOperator.IsEmpty => new Lambda(item =>
                    !TryGetProperty(item, field, out var emptyTarget) ||
                    emptyTarget.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                    (emptyTarget.ValueKind == JsonValueKind.String &&
                     string.IsNullOrWhiteSpace(emptyTarget.GetString()))),
                FilterOperator.IsNotEmpty => new Lambda(item =>
                    TryGetProperty(item, field, out var notEmptyTarget) &&
                    notEmptyTarget.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
                    (notEmptyTarget.ValueKind != JsonValueKind.String ||
                     !string.IsNullOrWhiteSpace(notEmptyTarget.GetString()))),
                FilterOperator.Equals => new Lambda(item => Compare(item, value, strict: true) == 0),
                FilterOperator.NotEquals => new Lambda(item => Compare(item, value, strict: true) != 0),
                FilterOperator.GreaterThan => new Lambda(item => Compare(item, value, strict: false) > 0),
                FilterOperator.GreaterThanOrEqual => new Lambda(item => Compare(item, value, strict: false) >= 0),
                FilterOperator.LessThan => new Lambda(item => Compare(item, value, strict: false) < 0),
                FilterOperator.LessThanOrEqual => new Lambda(item => Compare(item, value, strict: false) <= 0),
                FilterOperator.Contains => new Lambda(item =>
                    TryGetProperty(item, field, out var containsTarget) &&
                    containsTarget.ValueKind == JsonValueKind.String &&
                    containsTarget.GetString()?.Contains(value, StringComparison.OrdinalIgnoreCase) == true),
                FilterOperator.StartsWith => new Lambda(item =>
                    TryGetProperty(item, field, out var startsTarget) &&
                    startsTarget.ValueKind == JsonValueKind.String &&
                    startsTarget.GetString()?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true),
                FilterOperator.EndsWith => new Lambda(item =>
                    TryGetProperty(item, field, out var endsTarget) &&
                    endsTarget.ValueKind == JsonValueKind.String &&
                    endsTarget.GetString()?.EndsWith(value, StringComparison.OrdinalIgnoreCase) == true),
                _ => null
            };
        }

        /// <summary>Kind-aware comparison against the raw filter literal. Missing/null sorts lowest.</summary>
        private int Compare(in JsonElement item, string rawLiteral, bool strict)
        {
            if (!TryGetProperty(item, field, out var element))
            {
                return -1;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Number when decimal.TryParse(rawLiteral, NumberStyles.Number, CultureInfo.InvariantCulture, out var number):
                    return element.GetDecimal().CompareTo(number);
                case JsonValueKind.String:
                {
                    var text = element.GetString()!;
                    var comparison = string.Compare(text, rawLiteral, StringComparison.OrdinalIgnoreCase);
                    if (strict)
                    {
                        return text.Equals(rawLiteral, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    }

                    return Math.Sign(comparison);
                }
                case JsonValueKind.True or JsonValueKind.False when bool.TryParse(rawLiteral, out var flag):
                {
                    var current = element.GetBoolean();
                    return strict
                        ? (current == flag ? 0 : 1)
                        : current.CompareTo(flag);
                }
                default:
                    return strict ? 1 : -1;
            }
        }

        private sealed class Lambda(Func<JsonElement, bool> matches) : IFilterStrategy<JsonElement>
        {
            public bool Matches(JsonElement item) => matches(item);
        }
    }
}
