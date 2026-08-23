using System.Globalization;
using System.Text;

namespace NetOpenGrid.Host.Samples;

public static class EmployeeData
{
    public static IReadOnlyList<Employee> Create(int count = 247)
    {
        string[] firstNames =
        [
            "Ana", "Bruno", "Carla", "Diego", "Elena", "Fabio", "Gina", "Hugo",
            "Ines", "Jorge", "Karla", "Luis", "Marta", "Nico", "Olga", "Pablo",
            "Raul", "Rosa", "Sergio", "Tania", "Ulises", "Vera", "Walter", "Ximena"
        ];

        string[] lastNames =
        [
            "Garcia", "Rodriguez", "Martinez", "Lopez", "Gonzalez", "Perez",
            "Sanchez", "Ramirez", "Torres", "Flores", "Rivera", "Gomez",
            "Diaz", "Reyes", "Cruz", "Morales", "Ortiz", "Gutierrez"
        ];

        var random = new Random(42);
        var employees = new List<Employee>(count);

        for (var i = 0; i < count; i++)
        {
            var firstName = firstNames[i % firstNames.Length];
            var lastName = lastNames[(i / firstNames.Length + i) % lastNames.Length];
            var salary = Math.Round((45_000m + (decimal)(random.NextDouble() * 165_000)) / 500m) * 500m;
            var hiredOn = DateOnly.FromDateTime(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddDays(random.Next(4_200));

            employees.Add(new Employee(
                Id: Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                FullName: $"{firstName} {lastName}",
                Email: $"{firstName}.{lastName}{i}@acme.test".ToLowerInvariant(),
                Department: (Department)(i * 7 % 6),
                Salary: salary,
                HiredOn: hiredOn,
                PerformanceScore: Math.Round(random.NextDouble() * 4 + 1, 1),
                IsActive: random.NextDouble() > 0.15));
        }

        return employees;
    }

    public static readonly IReadOnlyList<Employee> All = Create();

    public static string ToCsv(IEnumerable<Employee> employees)
    {
        var csv = new StringBuilder("Id,Full name,Email,Department,Salary,Hired on,Score,Active\n");
        foreach (var e in employees)
        {
            csv.Append(e.Id).Append(',')
               .Append(EscapeCsv(e.FullName)).Append(',')
               .Append(e.Email).Append(',')
               .Append(e.Department).Append(',')
               .Append(e.Salary.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(e.HiredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
               .Append(e.PerformanceScore.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
               .Append(e.IsActive ? "true" : "false")
               .Append('\n');
        }

        return csv.ToString();
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') ? $"\"{value}\"" : value;
}
