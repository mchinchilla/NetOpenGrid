using System.Text.RegularExpressions;

namespace NetOpenGrid.Application.Builders;

internal static partial class ColumnNameHumanizer
{
    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelCasePattern();

    /// <summary>"BirthDate" / "birthDate" become "Birth date" (sentence case).</summary>
    internal static string Humanize(string field)
    {
        var spaced = CamelCasePattern().Replace(field, "$1 $2");
        var words = spaced.Split(' ');

        for (var i = 1; i < words.Length; i++)
        {
            if (words[i].Length > 0 && char.IsUpper(words[i][0]))
            {
                words[i] = char.ToLowerInvariant(words[i][0]) + words[i][1..];
            }
        }

        return string.Join(' ', words);
    }
}
