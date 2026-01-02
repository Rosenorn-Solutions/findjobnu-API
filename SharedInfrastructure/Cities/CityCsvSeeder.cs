using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;

namespace SharedInfrastructure.Cities;

public static class CityCsvSeeder
{
    private const string ResourceName = "SharedInfrastructure.Data.postal_codes_da.csv";

    public static async Task SeedAsync(DbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var set = context.Set<City>();
        var hasCities = await set.AsNoTracking().AnyAsync(cancellationToken);
        if (hasCities)
        {
            logger.LogInformation("City seeding skipped; Cities table already contains entries.");
            return;
        }

        var slugSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cities = await LoadCitiesAsync(slugSet, cancellationToken);
        if (cities.Count == 0)
        {
            logger.LogInformation("City seeding skipped; no new entries detected.");
            return;
        }

        await set.AddRangeAsync(cities, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} city entries from CSV.", cities.Count);
    }

    private static async Task<List<City>> LoadCitiesAsync(HashSet<string> existingSlugs, CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' missing. Ensure postal_codes_da.csv is marked as EmbeddedResource.");
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);

        var results = new List<City>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(',');
            if (separatorIndex < 0 || separatorIndex == line.Length - 1)
            {
                continue;
            }

            var cityName = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(cityName))
            {
                continue;
            }

            var slug = SlugHelper.ToSlug(cityName);
            if (string.IsNullOrEmpty(slug) || !existingSlugs.Add(slug))
            {
                continue;
            }

            results.Add(new City
            {
                Name = cityName,
                Slug = slug,
                ExternalId = DeterministicGuid.Create(slug)
            });
        }

        return results;
    }
}
