using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NetOpenGrid.Application.Binding;

namespace NetOpenGrid.Infrastructure.Binding;

public static class GridRequestValuesHttpExtensions
{
    public static GridRequestValues ToGridRequestValues(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Query;

        if (query.Count == 0)
        {
            return GridRequestValues.Empty;
        }

        var entries = new List<KeyValuePair<string, IReadOnlyList<string>>>(query.Count);
        foreach (var (key, values) in query)
        {
            var list = new List<string>(values.Count);
            foreach (var value in values)
            {
                if (value is not null)
                {
                    list.Add(value);
                }
            }

            entries.Add(new(key, list));
        }

        return new GridRequestValues(entries);
    }
}
