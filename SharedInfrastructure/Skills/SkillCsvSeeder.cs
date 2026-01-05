using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Cities;
using System.Reflection;
using System.Text;

namespace SharedInfrastructure.Skills;

public static class SkillCsvSeeder
{
    private const string ResourceName = "SharedInfrastructure.Data.skills_taxonomy.csv";

    public static async Task SeedAsync(DbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var skillSet = context.Set<CanonicalSkill>();
        var synonymSet = context.Set<SkillSynonym>();

        var hasSkills = await skillSet.AsNoTracking().AnyAsync(cancellationToken);
        if (hasSkills)
        {
            logger.LogInformation("Skill seeding skipped; CanonicalSkills table already contains entries.");
            return;
        }

        var (skills, synonyms) = await LoadSkillsAsync(cancellationToken);
        if (skills.Count == 0)
        {
            logger.LogInformation("Skill seeding skipped; no entries detected in CSV.");
            return;
        }

        await skillSet.AddRangeAsync(skills, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Now that skills have IDs, create synonym entries
        var skillLookup = skills.ToDictionary(s => s.Slug, s => s.Id, StringComparer.OrdinalIgnoreCase);
        var synonymEntities = new List<SkillSynonym>();

        foreach (var (slug, synonymList) in synonyms)
        {
            if (!skillLookup.TryGetValue(slug, out var skillId))
                continue;

            foreach (var syn in synonymList)
            {
                synonymEntities.Add(new SkillSynonym
                {
                    Synonym = syn.ToLowerInvariant(),
                    CanonicalSkillId = skillId
                });
            }
        }

        if (synonymEntities.Count > 0)
        {
            await synonymSet.AddRangeAsync(synonymEntities, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Seeded {SkillCount} canonical skills and {SynonymCount} synonyms from CSV.", skills.Count, synonymEntities.Count);
    }

    private static async Task<(List<CanonicalSkill> Skills, Dictionary<string, List<string>> Synonyms)> LoadSkillsAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' missing. Ensure skills_taxonomy.csv is marked as EmbeddedResource.");
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);

        var skills = new List<CanonicalSkill>();
        var synonyms = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var slugSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Skip header line
        var header = await reader.ReadLineAsync(cancellationToken);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = ParseCsvLine(line);
            if (parts.Count < 2)
                continue;

            var canonicalName = parts[0].Trim();
            var category = parts.Count > 1 ? parts[1].Trim() : null;
            var synonymsRaw = parts.Count > 2 ? parts[2].Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(canonicalName))
                continue;

            var slug = SlugHelper.ToSlug(canonicalName);
            if (string.IsNullOrEmpty(slug) || !slugSet.Add(slug))
                continue;

            var skill = new CanonicalSkill
            {
                Name = canonicalName,
                Slug = slug,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                ExternalId = DeterministicGuid.Create($"skill:{slug}")
            };

            skills.Add(skill);

            // Parse synonyms (pipe-delimited)
            if (!string.IsNullOrWhiteSpace(synonymsRaw))
            {
                var synList = synonymsRaw
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (synList.Count > 0)
                {
                    synonyms[slug] = synList;
                }
            }
        }

        return (skills, synonyms);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var results = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                results.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        results.Add(current.ToString());
        return results;
    }
}
