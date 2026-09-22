using System.Text.RegularExpressions;

namespace NetOpenGrid.Application.Builders;

internal static partial class ColumnNameHumanizer
{
    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex CamelCasePattern();

    /// <summary>
    /// "BirthDate", "birthDate" and "birthdate" all become sentence case: "Birth date",
    /// "Birth date", "Birthdate". The first letter is always capitalized, so a field written by
    /// hand ("email") reads the same as one derived from a member name ("Email").
    /// </summary>
    internal static string Humanize(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return field;
        }

        var spaced = CamelCasePattern().Replace(field, "$1 $2");
        var words = spaced.Split(' ');

        // Sentence case: capital on the first word only. Before, a member name arrived already
        // capitalized ("BirthDate") but a hand-written field ("email") did not, so the same
        // column read "Email" or "email" depending on how it was declared.
        if (words[0].Length > 0 && char.IsLower(words[0][0]))
        {
            words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        }

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
