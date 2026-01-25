using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace FindjobnuService.Services;

/// <summary>
/// Helper class for building optimized SQL queries for job search operations.
/// Encapsulates parameter management, WHERE clause construction, and cache key generation.
/// Supports both inline SQL and stored procedure execution.
/// </summary>
public sealed class JobSearchQueryBuilder
{
    private readonly List<string> _whereConditions = [];
    private readonly List<SqlParameter> _parameters = [];
    private readonly List<string?> _locationTokens = [];
    private readonly List<int> _categoryIds = [];
    private readonly List<string> _searchTermsLike = [];
    private int? _minRank;
    private int _locationIndex;
    private int _categoryIndex;
    private int _searchTermIndex;
    private int _page;
    private int _pageSize;
    private DateTime? _postedAfter;
    private DateTime? _postedBefore;

    /// <summary>
    /// Gets the full-text query string for CONTAINSTABLE.
    /// </summary>
    public string? FullTextQuery { get; private set; }

    /// <summary>
    /// Indicates whether any filters are applied (used for adaptive TOP_N_BY_RANK).
    /// </summary>
    public bool HasFilters => _whereConditions.Count > 0;

    /// <summary>
    /// Gets adaptive TOP_N_BY_RANK limit for main table based on filter presence.
    /// When filters are present, fewer full-text results are needed since filters will narrow them down.
    /// </summary>
    public int MainTableTopN => HasFilters ? 1000 : 2000;

    /// <summary>
    /// Gets adaptive TOP_N_BY_RANK limit for keywords table based on filter presence.
    /// </summary>
    public int KeywordsTableTopN => HasFilters ? 500 : 1000;

    /// <summary>
    /// Initializes the query builder with the full-text search terms.
    /// </summary>
    public JobSearchQueryBuilder WithFullTextQuery(IEnumerable<string>? searchTerms)
    {
        var terms = searchTerms?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => $"\"{t.Trim()}\"")
            .ToList();

        if (terms != null && terms.Count > 0)
        {
            FullTextQuery = string.Join(" OR ", terms);
            _parameters.Add(new SqlParameter("@ftQuery", FullTextQuery));
        }

        return this;
    }

    /// <summary>
    /// Adds a minimum rank threshold for full-text results.
    /// Only applied when value is greater than zero.
    /// </summary>
    public JobSearchQueryBuilder WithMinRank(int? minRank)
    {
        if (minRank.HasValue && minRank.Value > 0)
        {
            _minRank = minRank.Value;
            _parameters.Add(new SqlParameter("@minRank", minRank.Value));
        }

        return this;
    }

    /// <summary>
    /// Adds pagination parameters.
    /// </summary>
    public JobSearchQueryBuilder WithPagination(int page, int pageSize)
    {
        _page = page;
        _pageSize = pageSize;
        var offset = (page - 1) * pageSize;
        _parameters.Add(new SqlParameter("@off", offset));
        _parameters.Add(new SqlParameter("@take", pageSize));
        return this;
    }

    /// <summary>
    /// Adds date range filter for Published column.
    /// </summary>
    public JobSearchQueryBuilder WithDateRange(DateTime? postedAfter, DateTime? postedBefore)
    {
        _postedAfter = postedAfter;
        _postedBefore = postedBefore;

        if (postedAfter.HasValue)
        {
            _whereConditions.Add("j.Published >= @postedAfter");
            _parameters.Add(new SqlParameter("@postedAfter", postedAfter.Value));
        }

        if (postedBefore.HasValue)
        {
            _whereConditions.Add("j.Published <= @postedBefore");
            _parameters.Add(new SqlParameter("@postedBefore", postedBefore.Value));
        }

        return this;
    }

    /// <summary>
    /// Adds location filter with OR logic for multiple locations.
    /// Locations should be pre-normalized (first word/city name only).
    /// </summary>
    public JobSearchQueryBuilder WithLocations(IEnumerable<string?>? locationTokens)
    {
        var tokens = locationTokens?
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (tokens == null || tokens.Count == 0)
            return this;

        _locationTokens.AddRange(tokens);

        var locationConditions = new List<string>();
        foreach (var token in tokens)
        {
            var paramName = $"@location{_locationIndex++}";
            locationConditions.Add($"j.JobLocation LIKE '%' + {paramName} + '%'");
            _parameters.Add(new SqlParameter(paramName, token));
        }

        _whereConditions.Add($"({string.Join(" OR ", locationConditions)})");
        return this;
    }

    /// <summary>
    /// Adds category filter with OR logic for multiple category IDs.
    /// Uses EXISTS subquery for efficient filtering.
    /// </summary>
    public JobSearchQueryBuilder WithCategories(IEnumerable<int>? categoryIds)
    {
        var ids = categoryIds?
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids == null || ids.Count == 0)
            return this;

        _categoryIds.AddRange(ids);

        var categoryConditions = new List<string>();
        foreach (var id in ids)
        {
            var paramName = $"@categoryId{_categoryIndex++}";
            categoryConditions.Add($"jc.CategoryID = {paramName}");
            _parameters.Add(new SqlParameter(paramName, id));
        }

        _whereConditions.Add($"EXISTS (SELECT 1 FROM dbo.JobCategories jc WHERE jc.JobID = j.JobID AND ({string.Join(" OR ", categoryConditions)}))");
        return this;
    }

    /// <summary>
    /// Adds additional search term filter with OR logic (LIKE-based, not full-text).
    /// Used for recommendations to filter on top of profile keywords.
    /// </summary>
    public JobSearchQueryBuilder WithSearchTermsLike(IEnumerable<string>? searchTerms)
    {
        var terms = searchTerms?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        if (terms == null || terms.Count == 0)
            return this;

        _searchTermsLike.AddRange(terms);

        var searchConditions = new List<string>();
        foreach (var term in terms)
        {
            var paramName = $"@searchTerm{_searchTermIndex++}";
            searchConditions.Add($"(j.JobTitle LIKE '%' + {paramName} + '%' OR j.CompanyName LIKE '%' + {paramName} + '%' OR j.JobDescription LIKE '%' + {paramName} + '%')");
            _parameters.Add(new SqlParameter(paramName, term));
        }

        _whereConditions.Add($"({string.Join(" OR ", searchConditions)})");
        return this;
    }

    /// <summary>
    /// Builds the WHERE clause string (includes "WHERE " prefix if conditions exist).
    /// </summary>
    public string BuildWhereClause()
    {
        return _whereConditions.Count > 0
            ? "WHERE " + string.Join(" AND ", _whereConditions)
            : "";
    }

    /// <summary>
    /// Gets all parameters as an array for SQL execution.
    /// </summary>
    public SqlParameter[] GetParameters() => [.. _parameters];

    /// <summary>
    /// Gets parameters for count query (excludes pagination parameters).
    /// Creates new SqlParameter instances to avoid reuse issues.
    /// </summary>
    public SqlParameter[] GetCountParameters()
    {
        return _parameters
            .Where(p => p.ParameterName != "@off" && p.ParameterName != "@take")
            .Select(p => new SqlParameter(p.ParameterName, p.Value))
            .ToArray();
    }

    /// <summary>
    /// Gets parameters for stored procedure execution.
    /// </summary>
    public SqlParameter[] GetStoredProcedureParameters(bool includeSearchTerms = false)
    {
        var parameters = new List<SqlParameter>
        {
            new("@ftQuery", FullTextQuery ?? ""),
            new("@locations", _locationTokens.Count > 0 ? string.Join(",", _locationTokens.Where(l => l != null)) : DBNull.Value),
            new("@categoryIds", _categoryIds.Count > 0 ? string.Join(",", _categoryIds) : DBNull.Value),
            new("@postedAfter", _postedAfter.HasValue ? _postedAfter.Value : DBNull.Value),
            new("@postedBefore", _postedBefore.HasValue ? _postedBefore.Value : DBNull.Value),
            new("@offset", (_page - 1) * _pageSize),
            new("@take", _pageSize),
            new("@mainTableTopN", MainTableTopN),
            new("@keywordsTableTopN", KeywordsTableTopN),
            new("@minRank", _minRank.HasValue ? _minRank.Value : 0),
            new("@totalCount", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output }
        };

        if (includeSearchTerms)
        {
            parameters.Insert(1, new SqlParameter("@searchTerms", 
                _searchTermsLike.Count > 0 ? string.Join(",", _searchTermsLike) : DBNull.Value));
        }

        return [.. parameters];
    }

    /// <summary>
    /// Generates a hash-based cache key for the current query configuration.
    /// More efficient than string concatenation for complex queries.
    /// </summary>
    public static string GenerateCacheKey(
        string prefix,
        IEnumerable<string>? searchTerms,
        IEnumerable<string?>? locations,
        IEnumerable<int>? categoryIds,
        DateTime? postedAfter,
        DateTime? postedBefore,
        int page,
        int pageSize)
    {
        var sb = new StringBuilder(256);
        sb.Append(prefix);

        if (searchTerms != null)
        {
            foreach (var term in searchTerms.Where(t => !string.IsNullOrWhiteSpace(t)).OrderBy(t => t))
            {
                sb.Append('|').Append(term);
            }
        }

        sb.Append("||");

        if (locations != null)
        {
            foreach (var loc in locations.Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l))
            {
                sb.Append('|').Append(loc);
            }
        }

        sb.Append("||");

        if (categoryIds != null)
        {
            foreach (var id in categoryIds.Where(id => id > 0).OrderBy(id => id))
            {
                sb.Append('|').Append(id);
            }
        }

        sb.Append("||");
        sb.Append(postedAfter?.Ticks ?? 0);
        sb.Append('|');
        sb.Append(postedBefore?.Ticks ?? 0);
        sb.Append('|');
        sb.Append(page);
        sb.Append('|');
        sb.Append(pageSize);

        // Use SHA256 for consistent, short cache keys
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"{prefix}:{Convert.ToHexString(hash)[..16]}";
    }

    /// <summary>
    /// Builds the optimized search SQL with COUNT(*) OVER() for single-query execution.
    /// Returns both data and total count in one database round-trip.
    /// </summary>
    public string BuildSearchSqlWithCount()
    {
        var whereClause = BuildWhereClause();

        return $@"
;WITH RankedResults AS (
    SELECT j.JobID, MAX(r.[RANK]) AS [RANK]
    FROM (
        SELECT t.[KEY] AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, {MainTableTopN}) t
        UNION
        SELECT j.JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, {KeywordsTableTopN}) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ) r
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
    {whereClause}
    GROUP BY j.JobID
),
CountedResults AS (
    SELECT JobID, [RANK], COUNT(*) OVER() AS TotalCount
    FROM RankedResults
)
SELECT j.*, cr.TotalCount
FROM CountedResults cr
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = cr.JobID
ORDER BY cr.[RANK] DESC, j.Published DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";
    }

    /// <summary>
    /// Builds the recommendations SQL with COUNT(*) OVER() for single-query execution.
    /// </summary>
    public string BuildRecommendationsSqlWithCount()
    {
        var whereClause = BuildWhereClause();
        var havingRank = _minRank.HasValue ? "HAVING MAX(r.[RANK]) >= @minRank" : string.Empty;

        return $@"
;WITH RankedResults AS (
    SELECT j.JobID, MAX(r.[RANK]) AS [RANK]
    FROM (
        SELECT t.[KEY] AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, {MainTableTopN}) t
        UNION
        SELECT j.JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, {KeywordsTableTopN}) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ) r
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
    {whereClause}
    GROUP BY j.JobID
    {havingRank}
),
CountedResults AS (
    SELECT JobID, [RANK], COUNT(*) OVER() AS TotalCount
    FROM RankedResults
)
SELECT j.*, cr.TotalCount
FROM CountedResults cr
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = cr.JobID
ORDER BY cr.[RANK] DESC, j.Published DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";
    }

    /// <summary>
    /// Builds the stored procedure call for search.
    /// Stored procedures provide query plan caching benefits.
    /// </summary>
    public string BuildSearchStoredProcedureCall()
    {
        return "EXEC dbo.usp_SearchJobs @ftQuery, @locations, @categoryIds, @postedAfter, @postedBefore, @offset, @take, @mainTableTopN, @keywordsTableTopN, @totalCount OUTPUT";
    }

    /// <summary>
    /// Builds the stored procedure call for recommendations.
    /// </summary>
    public string BuildRecommendationsStoredProcedureCall()
    {
        return "EXEC dbo.usp_GetRecommendedJobs @ftQuery, @searchTerms, @locations, @categoryIds, @postedAfter, @postedBefore, @offset, @take, @mainTableTopN, @keywordsTableTopN, @minRank, @totalCount OUTPUT";
    }
}
