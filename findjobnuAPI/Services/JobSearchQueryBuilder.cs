using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace FindjobnuService.Services;

public sealed class JobSearchQueryBuilder
{
    private readonly List<string> _whereConditions = [];
    private readonly List<SqlParameter> _parameters = [];
    private readonly List<string?> _locationTokens = [];
    private readonly List<string> _categoryKeys = [];
    private readonly List<string> _searchTermsLike = [];
    private int? _minRank;
    private int _locationIndex;
    private int _categoryIndex;
    private int _searchTermIndex;
    private int _page;
    private int _pageSize;
    private DateTime? _postedAfter;
    private DateTime? _postedBefore;

    public string? FullTextQuery { get; private set; }
    public bool HasFilters => _whereConditions.Count > 0;
    public int MainTableTopN => HasFilters ? 1000 : 2000;
    public int KeywordsTableTopN => HasFilters ? 500 : 1000;

    public JobSearchQueryBuilder WithFullTextQuery(IEnumerable<string>? searchTerms)
    {
        var terms = searchTerms?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => $"\"{t.Trim()}\"")
            .ToList();

        if (terms is { Count: > 0 })
        {
            FullTextQuery = string.Join(" OR ", terms);
            _parameters.Add(new SqlParameter("@ftQuery", FullTextQuery));
        }

        return this;
    }

    public JobSearchQueryBuilder WithMinRank(int? minRank)
    {
        if (minRank.HasValue && minRank.Value > 0)
        {
            _minRank = minRank.Value;
            _parameters.Add(new SqlParameter("@minRank", minRank.Value));
        }

        return this;
    }

    public JobSearchQueryBuilder WithPagination(int page, int pageSize)
    {
        _page = page;
        _pageSize = pageSize;
        _parameters.Add(new SqlParameter("@off", (page - 1) * pageSize));
        _parameters.Add(new SqlParameter("@take", pageSize));
        return this;
    }

    public JobSearchQueryBuilder WithDateRange(DateTime? postedAfter, DateTime? postedBefore)
    {
        _postedAfter = postedAfter;
        _postedBefore = postedBefore;

        if (postedAfter.HasValue)
        {
            _whereConditions.Add("s.published_utc >= @postedAfter");
            _parameters.Add(new SqlParameter("@postedAfter", postedAfter.Value));
        }

        if (postedBefore.HasValue)
        {
            _whereConditions.Add("s.published_utc <= @postedBefore");
            _parameters.Add(new SqlParameter("@postedBefore", postedBefore.Value));
        }

        return this;
    }

    public JobSearchQueryBuilder WithLocations(IEnumerable<string?>? locationTokens)
    {
        var tokens = locationTokens?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (tokens is not { Count: > 0 })
        {
            return this;
        }

        _locationTokens.AddRange(tokens);

        var conditions = new List<string>();
        foreach (var token in tokens)
        {
            var paramName = $"@location{_locationIndex++}";
            conditions.Add($"s.location_normalized LIKE '%' + {paramName} + '%' ");
            _parameters.Add(new SqlParameter(paramName, token));
        }

        _whereConditions.Add($"({string.Join(" OR ", conditions)})");
        return this;
    }

    public JobSearchQueryBuilder WithCategories(IEnumerable<string>? categoryKeys)
    {
        var keys = categoryKeys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keys is not { Count: > 0 })
        {
            return this;
        }

        _categoryKeys.AddRange(keys);

        var conditions = new List<string>();
        foreach (var key in keys)
        {
            var paramName = $"@categoryKey{_categoryIndex++}";
            conditions.Add($"c.category_key = {paramName}");
            _parameters.Add(new SqlParameter(paramName, key));
        }

        _whereConditions.Add($"EXISTS (SELECT 1 FROM dbo.job_categories jc JOIN dbo.categories c ON c.category_id = jc.category_id WHERE jc.job_id = j.job_id AND ({string.Join(" OR ", conditions)}))");
        return this;
    }

    public JobSearchQueryBuilder WithSearchTermsLike(IEnumerable<string>? searchTerms)
    {
        var terms = searchTerms?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        if (terms is not { Count: > 0 })
        {
            return this;
        }

        _searchTermsLike.AddRange(terms);

        var conditions = new List<string>();
        foreach (var term in terms)
        {
            var paramName = $"@searchTerm{_searchTermIndex++}";
            conditions.Add($"(s.job_title_normalized LIKE '%' + {paramName} + '%' OR s.company_name_normalized LIKE '%' + {paramName} + '%' OR s.job_description_clean LIKE '%' + {paramName} + '%')");
            _parameters.Add(new SqlParameter(paramName, term));
        }

        _whereConditions.Add($"({string.Join(" OR ", conditions)})");
        return this;
    }

    public string BuildWhereClause() => _whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", _whereConditions) : string.Empty;
    public SqlParameter[] GetParameters() => [.. _parameters];

    public SqlParameter[] GetCountParameters()
        => [.. _parameters.Where(p => p.ParameterName != "@off" && p.ParameterName != "@take").Select(p => new SqlParameter(p.ParameterName, p.Value))];

    public SqlParameter[] GetStoredProcedureParameters(bool includeSearchTerms = false)
    {
        var parameters = new List<SqlParameter>
        {
            new("@ftQuery", FullTextQuery ?? string.Empty),
            new("@locations", _locationTokens.Count > 0 ? string.Join(',', _locationTokens.Where(l => l != null)) : DBNull.Value),
            new("@categoryKeys", _categoryKeys.Count > 0 ? string.Join(',', _categoryKeys) : DBNull.Value),
            new("@postedAfter", _postedAfter ?? (object)DBNull.Value),
            new("@postedBefore", _postedBefore ?? (object)DBNull.Value),
            new("@offset", (_page - 1) * _pageSize),
            new("@take", _pageSize),
            new("@mainTableTopN", MainTableTopN),
            new("@keywordsTableTopN", KeywordsTableTopN),
            new("@minRank", _minRank ?? 0),
            new("@totalCount", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output }
        };

        if (includeSearchTerms)
        {
            parameters.Insert(1, new SqlParameter("@searchTerms", _searchTermsLike.Count > 0 ? string.Join(',', _searchTermsLike) : DBNull.Value));
        }

        return [.. parameters];
    }

    public static string GenerateCacheKey(string prefix, IEnumerable<string>? searchTerms, IEnumerable<string?>? locations, IEnumerable<string>? categoryKeys, DateTime? postedAfter, DateTime? postedBefore, int page, int pageSize)
    {
        var sb = new StringBuilder(256).Append(prefix);

        AppendValues(sb, searchTerms);
        sb.Append("||");
        AppendValues(sb, locations);
        sb.Append("||");
        AppendValues(sb, categoryKeys);
        sb.Append("||").Append(postedAfter?.Ticks ?? 0).Append('|').Append(postedBefore?.Ticks ?? 0).Append('|').Append(page).Append('|').Append(pageSize);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"{prefix}:{Convert.ToHexString(hash)[..16]}";
    }

    public string BuildSearchSqlWithCount()
    {
        var whereClause = BuildWhereClause();
        return $@"
;WITH RankedResults AS (
    SELECT j.job_id AS JobID, MAX(r.[RANK]) AS [RANK]
    FROM (
        SELECT s.job_id AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.job_snapshots, (job_title_normalized, job_description_clean, company_name_normalized, location_normalized), @ftQuery, {MainTableTopN}) t
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = t.[KEY]
        UNION
        SELECT s.job_id AS JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.job_keywords, keyword, @ftQuery, {KeywordsTableTopN}) tk
        JOIN dbo.job_keywords k ON k.job_keyword_id = tk.[KEY]
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = k.job_snapshot_id
    ) r
    JOIN dbo.jobs j ON j.job_id = r.JobID
    JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
    WHERE j.is_active = 1 AND j.current_snapshot_id IS NOT NULL
    {(string.IsNullOrWhiteSpace(whereClause) ? string.Empty : "AND " + whereClause[6..])}
    GROUP BY j.job_id
),
CountedResults AS (
    SELECT JobID, [RANK], COUNT(*) OVER() AS TotalCount
    FROM RankedResults
)
SELECT j.job_id AS JobID,
       s.company_name_normalized AS CompanyName,
       s.company_url_normalized AS CompanyURL,
       s.job_title_normalized AS JobTitle,
       s.job_description_clean AS JobDescription,
       s.location_normalized AS JobLocation,
       j.canonical_job_url AS JobUrl,
       s.published_utc AS Published,
       CASE WHEN s.banner_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/banner') END AS BannerImageUrl,
       CASE WHEN s.footer_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/footer') END AS FooterImageUrl,
       j.source_host AS SourceHost,
       cr.TotalCount
FROM CountedResults cr
JOIN dbo.jobs j ON j.job_id = cr.JobID
JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
ORDER BY cr.[RANK] DESC, s.published_utc DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";
    }

    public string BuildRecommendationsSqlWithCount()
    {
        var whereClause = BuildWhereClause();
        var havingRank = _minRank.HasValue ? "HAVING MAX(r.[RANK]) >= @minRank" : string.Empty;
        return $@"
;WITH RankedResults AS (
    SELECT j.job_id AS JobID, MAX(r.[RANK]) AS [RANK]
    FROM (
        SELECT s.job_id AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.job_snapshots, (job_title_normalized, job_description_clean, company_name_normalized, location_normalized), @ftQuery, {MainTableTopN}) t
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = t.[KEY]
        UNION
        SELECT s.job_id AS JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.job_keywords, keyword, @ftQuery, {KeywordsTableTopN}) tk
        JOIN dbo.job_keywords k ON k.job_keyword_id = tk.[KEY]
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = k.job_snapshot_id
    ) r
    JOIN dbo.jobs j ON j.job_id = r.JobID
    JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
    WHERE j.is_active = 1 AND j.current_snapshot_id IS NOT NULL
    {(string.IsNullOrWhiteSpace(whereClause) ? string.Empty : "AND " + whereClause[6..])}
    GROUP BY j.job_id
    {havingRank}
),
CountedResults AS (
    SELECT JobID, [RANK], COUNT(*) OVER() AS TotalCount
    FROM RankedResults
)
SELECT j.job_id AS JobID,
       s.company_name_normalized AS CompanyName,
       s.company_url_normalized AS CompanyURL,
       s.job_title_normalized AS JobTitle,
       s.job_description_clean AS JobDescription,
       s.location_normalized AS JobLocation,
       j.canonical_job_url AS JobUrl,
       s.published_utc AS Published,
       CASE WHEN s.banner_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/banner') END AS BannerImageUrl,
       CASE WHEN s.footer_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/footer') END AS FooterImageUrl,
       j.source_host AS SourceHost,
       cr.TotalCount
FROM CountedResults cr
JOIN dbo.jobs j ON j.job_id = cr.JobID
JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
ORDER BY cr.[RANK] DESC, s.published_utc DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";
    }

    public string BuildSearchStoredProcedureCall()
        => "EXEC dbo.usp_SearchJobs @ftQuery, @locations, @categoryKeys, @postedAfter, @postedBefore, @offset, @take, @mainTableTopN, @keywordsTableTopN, @totalCount OUTPUT";

    public string BuildRecommendationsStoredProcedureCall()
        => "EXEC dbo.usp_GetRecommendedJobs @ftQuery, @searchTerms, @locations, @categoryKeys, @postedAfter, @postedBefore, @offset, @take, @mainTableTopN, @keywordsTableTopN, @minRank, @totalCount OUTPUT";

    private static void AppendValues(StringBuilder sb, IEnumerable<string?>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).OrderBy(v => v))
        {
            sb.Append('|').Append(value);
        }
    }
}
