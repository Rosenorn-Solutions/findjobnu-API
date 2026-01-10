using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FindjobnuService.Services;

public interface ISkillTaxonomy
{
    IReadOnlyCollection<string> CanonicalSkills { get; }
    bool TryNormalize(string? raw, out string canonical);
    Task RefreshCacheAsync(CancellationToken cancellationToken = default);
}

public sealed class SkillTaxonomy : ISkillTaxonomy
{
    private readonly IDbContextFactory<Repositories.Context.FindjobnuContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "SkillTaxonomy";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private record TaxonomyData(
        IReadOnlyCollection<string> CanonicalSkills,
        Dictionary<string, string> CanonicalLookup,
        Dictionary<string, string> SynonymToCanonical);

    public SkillTaxonomy(IDbContextFactory<Repositories.Context.FindjobnuContext> contextFactory, IMemoryCache cache)
    {
        _contextFactory = contextFactory;
        _cache = cache;
    }

    public IReadOnlyCollection<string> CanonicalSkills => GetData().CanonicalSkills;

    public bool TryNormalize(string? raw, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var key = NormalizeKey(raw);
        var data = GetData();

        if (data.SynonymToCanonical.TryGetValue(key, out var mapped))
        {
            canonical = mapped;
            return true;
        }

        if (data.CanonicalLookup.TryGetValue(key, out var direct))
        {
            canonical = direct;
            return true;
        }

        return false;
    }

    public async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        var data = await LoadFromDatabaseAsync(cancellationToken);
        _cache.Set(CacheKey, data, CacheDuration);
    }

    private TaxonomyData GetData()
    {
        if (_cache.TryGetValue<TaxonomyData>(CacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        // Synchronous fallback - should rarely happen after startup
        var data = LoadFromDatabaseAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cache.Set(CacheKey, data, CacheDuration);
        return data;
    }

    private async Task<TaxonomyData> LoadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var skills = await context.CanonicalSkills
            .AsNoTracking()
            .Include(s => s.Synonyms)
            .ToListAsync(cancellationToken);

        var canonicalSkills = skills.Select(s => s.Name).ToList();
        var canonicalLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var synonymToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in skills)
        {
            var key = NormalizeKey(skill.Name);
            canonicalLookup[key] = skill.Name;

            foreach (var syn in skill.Synonyms)
            {
                var synKey = NormalizeKey(syn.Synonym);
                synonymToCanonical[synKey] = skill.Name;
            }
        }

        return new TaxonomyData(canonicalSkills, canonicalLookup, synonymToCanonical);
    }

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();
}
