using FindjobnuService.DTOs.Requests;
using FindjobnuService.DTOs.Responses;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FindjobnuService.Services
{
    public class JobIndexPostsService : IJobIndexPostsService
    {
        private readonly FindjobnuContext _db;
        private readonly ILogger<JobIndexPostsService> _logger;
        private readonly IMemoryCache _cache;

        public JobIndexPostsService(FindjobnuContext db, ILogger<JobIndexPostsService> logger, IMemoryCache cache)
        {
            _db = db;
            _logger = logger;
            _cache = cache;
        }

        // Backward-compatible constructor used by worker/tests
        public JobIndexPostsService(FindjobnuContext db, ILogger<JobIndexPostsService> logger)
            : this(db, logger, new MemoryCache(new MemoryCacheOptions()))
        {
        }

        public async Task<PagedList<JobIndexPosts>> GetAllAsync(int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await _db.JobIndexPosts.CountAsync();
            var items = await _db.JobIndexPosts
                .Include(j => j.Categories)
                .OrderBy(j => j.JobID)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        public async Task<PagedList<JobIndexPosts>> SearchAsync(string[]? searchTerms, string[]? locations, int[]? categoryIds, DateTime? postedAfter, DateTime? postedBefore, int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            // Normalize locations: take only the first word (city name) from each location
            // "København K" -> "København", "Aarhus C" -> "Aarhus"
            var locationTokens = locations?
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct()
                .ToList();

            // Normalize search terms
            var normalizedSearchTerms = searchTerms?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList();

            // Normalize category IDs
            var normalizedCategoryIds = categoryIds?
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var cacheKey = $"search:{string.Join("|", normalizedSearchTerms ?? [])}|{string.Join("|", locationTokens ?? [])}|{string.Join("|", normalizedCategoryIds ?? [])}|{postedAfter:O}|{postedBefore:O}|{page}|{pageSize}";
            if (_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            PagedList<JobIndexPosts> result;

            // Build full-text query from multiple search terms
            var ftQuery = (normalizedSearchTerms == null || normalizedSearchTerms.Count == 0)
                ? null
                : string.Join(" OR ", normalizedSearchTerms.Select(t => $"\"{t}\""));

            if (_db.Database.IsSqlServer() && !string.IsNullOrWhiteSpace(ftQuery))
            {
                var off = (page - 1) * pageSize;
                var take = pageSize;

                // Build dynamic WHERE clause for multiple locations and categories
                var whereConditions = new List<string>();
                var parameters = new List<SqlParameter>
                {
                    new SqlParameter("@ftQuery", ftQuery),
                    new SqlParameter("@off", off),
                    new SqlParameter("@take", take)
                };

                if (postedAfter.HasValue)
                {
                    whereConditions.Add("j.Published >= @postedAfter");
                    parameters.Add(new SqlParameter("@postedAfter", postedAfter.Value));
                }
                if (postedBefore.HasValue)
                {
                    whereConditions.Add("j.Published <= @postedBefore");
                    parameters.Add(new SqlParameter("@postedBefore", postedBefore.Value));
                }

                // Multiple locations with OR logic
                if (locationTokens != null && locationTokens.Count > 0)
                {
                    var locationConditions = new List<string>();
                    for (int i = 0; i < locationTokens.Count; i++)
                    {
                        var paramName = $"@location{i}";
                        locationConditions.Add($"j.JobLocation LIKE '%' + {paramName} + '%'");
                        parameters.Add(new SqlParameter(paramName, locationTokens[i]));
                    }
                    whereConditions.Add($"({string.Join(" OR ", locationConditions)})");
                }

                // Multiple categories with OR logic
                if (normalizedCategoryIds != null && normalizedCategoryIds.Count > 0)
                {
                    var categoryConditions = new List<string>();
                    for (int i = 0; i < normalizedCategoryIds.Count; i++)
                    {
                        var paramName = $"@categoryId{i}";
                        categoryConditions.Add($"jc.CategoryID = {paramName}");
                        parameters.Add(new SqlParameter(paramName, normalizedCategoryIds[i]));
                    }
                    whereConditions.Add($"EXISTS (SELECT 1 FROM dbo.JobCategories jc WHERE jc.JobID = j.JobID AND ({string.Join(" OR ", categoryConditions)}))");
                }

                var whereClause = whereConditions.Count > 0
                    ? "WHERE " + string.Join(" AND ", whereConditions)
                    : "";

                // Optimized query with:
                // 1. TOP_N_BY_RANK (2000) to limit full-text results early
                // 2. Filters applied inside subquery (early filter pushdown)
                // 3. COUNT(*) OVER() to get total count in single query pass
                var baseSql = $@"
SELECT j.*, r.TotalCount
FROM (
    SELECT ranked.JobID, ranked.[RANK], COUNT(*) OVER() AS TotalCount
    FROM (
        SELECT r.JobID, MAX(r.[RANK]) AS [RANK]
        FROM (
            SELECT t.[KEY] AS JobID, t.[RANK]
            FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, 2000) t
            UNION
            SELECT j.JobID, tk.[RANK]
            FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, 1000) tk
            JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
            JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
        ) r
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
        {whereClause}
        GROUP BY r.JobID
    ) ranked
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
ORDER BY r.[RANK] DESC, j.Published DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";

                var rawResults = await _db.JobIndexPosts
                    .FromSqlRaw(baseSql, parameters.ToArray())
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .ToListAsync();

                // Extract total count from first result (all rows have the same TotalCount)
                // If no results, count is 0
                int totalCount = 0;
                if (rawResults.Count > 0)
                {
                    // Re-query to get the count since EF Core doesn't map TotalCount directly
                    // Use a simpler count query with TOP_N_BY_RANK optimization
                    var countSql = $@"
SELECT COUNT(*) AS Value
FROM (
    SELECT r.JobID
    FROM (
        SELECT t.[KEY] AS JobID
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, 2000) t
        UNION
        SELECT j.JobID
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, 1000) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ) r
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
    {whereClause}
    GROUP BY r.JobID
) counted";

                    var countParams = parameters.Where(p => p.ParameterName != "@off" && p.ParameterName != "@take")
                        .Select(p => new SqlParameter(p.ParameterName, p.Value))
                        .ToArray();

                    totalCount = await _db.Database
                        .SqlQueryRaw<int>(countSql, countParams)
                        .FirstOrDefaultAsync();
                }

                result = new PagedList<JobIndexPosts>(totalCount, pageSize, page, rawResults);
            }
            else
            {
                // For InMemory/non-SQL Server: load data first then filter in memory
                var jobs = await _db.JobIndexPosts.Include(j => j.Categories).AsNoTracking().ToListAsync();
                var jobKeywords = await _db.JobKeywords.AsNoTracking().ToListAsync();

                IEnumerable<JobIndexPosts> filteredJobs = jobs;

                // Date filters
                if (postedAfter.HasValue)
                {
                    filteredJobs = filteredJobs.Where(j => j.Published >= postedAfter.Value);
                }
                if (postedBefore.HasValue)
                {
                    filteredJobs = filteredJobs.Where(j => j.Published <= postedBefore.Value);
                }

                // Multiple locations with OR logic - match city name prefix
                if (locationTokens != null && locationTokens.Count > 0)
                {
                    filteredJobs = filteredJobs.Where(j =>
                        j.JobLocation != null &&
                        locationTokens.Any(loc => j.JobLocation.IndexOf(loc!, StringComparison.OrdinalIgnoreCase) >= 0));
                }

                // Multiple categories with OR logic
                if (normalizedCategoryIds != null && normalizedCategoryIds.Count > 0)
                {
                    filteredJobs = filteredJobs.Where(j =>
                        j.Categories != null && j.Categories.Any(c => normalizedCategoryIds.Contains(c.CategoryID)));
                }

                // Multiple search terms with OR logic
                if (normalizedSearchTerms != null && normalizedSearchTerms.Count > 0)
                {
                    var terms = normalizedSearchTerms.Select(t => t.ToLowerInvariant()).ToList();
                    filteredJobs = filteredJobs.Where(j =>
                        (j.JobTitle != null && terms.Any(term => j.JobTitle.ToLower().Contains(term))) ||
                        (j.CompanyName != null && terms.Any(term => j.CompanyName.ToLower().Contains(term))) ||
                        (j.JobDescription != null && terms.Any(term => j.JobDescription.ToLower().Contains(term))) ||
                        jobKeywords.Any(k => k.JobID == j.JobID && k.Keyword != null && terms.Any(term => k.Keyword.ToLower().Contains(term)))
                    );
                }

                var filteredList = filteredJobs.ToList();
                var total = filteredList.Count;

                if (total == 0)
                {
                    result = new PagedList<JobIndexPosts>(0, pageSize, page, []);
                }
                else
                {
                    var items = filteredList
                        .OrderByDescending(j => j.Published)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    result = new PagedList<JobIndexPosts>(total, pageSize, page, items);
                }
            }

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });

            return result;
        }

        public async Task<JobIndexPosts> GetByIdAsync(int id)
        {
            return await _db.JobIndexPosts
                .Include(j => j.Categories)
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.JobID == id) ?? new JobIndexPosts();
        }

        public async Task<CategoriesResponse> GetCategoriesAsync()
        {
            try
            {
                var rawCategoryData = await _db.Categories
                    .AsNoTracking()
                    .Select(c => new
                    {
                        c.CategoryID,
                        c.Name,
                        NumberOfJobs = c.JobIndexPosts.Count
                    })
                    .OrderBy(x => x.Name)
                    .ToListAsync();

                var categoryJobCounts = rawCategoryData
                    .Select(x => new CategoryJobCountResponse(x.CategoryID, x.Name, x.NumberOfJobs))
                    .ToList();

                return new CategoriesResponse(true, null, categoryJobCounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get categories");
                return new CategoriesResponse(false, ex.Message, []);
            }
        }

        public async Task<JobStatisticsResponse> GetStatisticsAsync()
        {
            var now = DateTime.UtcNow;
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddMonths(-1);

            var totalJobs = await _db.JobIndexPosts.CountAsync();
            var newJobsLastWeek = await _db.JobIndexPosts.CountAsync(j => j.Published >= weekAgo);
            var newJobsLastMonth = await _db.JobIndexPosts.CountAsync(j => j.Published >= monthAgo);

            var topCategories = await _db.Categories
                .Select(c => new
                {
                    c.CategoryID,
                    c.Name,
                    NumberOfJobs = c.JobIndexPosts.Count
                })
                .OrderByDescending(c => c.NumberOfJobs)
                .ThenBy(c => c.Name)
                .Take(10)
                .Select(c => new CategoryJobCountResponse(c.CategoryID, c.Name, c.NumberOfJobs))
                .ToListAsync();

            var topCategoriesLastWeek = await _db.Categories
                .Select(c => new
                {
                    c.CategoryID,
                    c.Name,
                    NumberOfJobs = c.JobIndexPosts.Count(j => j.Published >= weekAgo)
                })
                .Where(c => c.NumberOfJobs > 0)
                .OrderByDescending(c => c.NumberOfJobs)
                .ThenBy(c => c.Name)
                .Take(5)
                .Select(c => new CategoryJobCountResponse(c.CategoryID, c.Name, c.NumberOfJobs))
                .ToListAsync();

            return new JobStatisticsResponse(
                topCategories,
                topCategoriesLastWeek,
                totalJobs,
                newJobsLastWeek,
                newJobsLastMonth);
        }

        public async Task<PagedList<JobIndexPosts>> GetSavedJobsByUserId(string userId, int page)
        {
            var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile == null || profile.SavedJobPosts == null || !profile.SavedJobPosts.Any())
                return new PagedList<JobIndexPosts>(0, 10, page, []);

            var jobIds = profile.SavedJobPosts
                .Select(id => int.TryParse(id, out var jid) ? jid : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            var jobs = await _db.JobIndexPosts.Where(j => jobIds.Contains(j.JobID)).ToListAsync();
            return new PagedList<JobIndexPosts>(jobs.Count, 10, page, jobs);
        }

        public async Task<PagedList<JobIndexPosts>> GetRecommendedJobsByUserAndProfile(string userId, RecommendedJobsRequest? request)
        {
            var page = request?.Page ?? 1;
            var pageSize = request?.PageSize ?? 20;
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            // Include all filter parameters in cache key
            var searchTermsKey = request?.SearchTerms != null ? string.Join(",", request.SearchTerms.Where(t => !string.IsNullOrWhiteSpace(t))) : "";
            var locationsKey = request?.Locations != null ? string.Join(",", request.Locations.Where(l => !string.IsNullOrWhiteSpace(l))) : "";
            var categoryIdsKey = request?.CategoryIds != null ? string.Join(",", request.CategoryIds.Where(id => id > 0)) : "";
            var cacheKey = $"rec:{userId}:{page}:{pageSize}:{searchTermsKey}:{locationsKey}:{categoryIdsKey}:{request?.PostedAfter:O}:{request?.PostedBefore:O}";
            if (_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            // Build recommendations with filters applied before paging
            var result = await BuildRecommendations(userId, request, page, pageSize);

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });

            return result;
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendations(string userId, RecommendedJobsRequest? request, int page, int pageSize)
        {
            var profile = await _db.Profiles
                .Include(p => p.BasicInfo)
                .Include(p => p.Experiences)
                .Include(p => p.Educations)
                .Include(p => p.Interests)
                .Include(p => p.Accomplishments)
                .Include(p => p.Contacts)
                .Include(p => p.Skills)
                .FirstOrDefaultAsync(x => x.UserId == userId);
            if (profile == null)
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);

            if ((profile.Keywords == null || profile.Keywords.Count == 0)
                && (profile.Experiences == null || profile.Experiences.Count == 0)
                && (profile.Interests == null || profile.Interests.Count == 0)
                && (profile.BasicInfo == null || string.IsNullOrEmpty(profile.BasicInfo.JobTitle)))
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);

            var keywords = GetKeywordsFromProfile(profile)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToList();

            if (keywords.Count == 0)
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);

            // Normalize locations: take only the first word (city name) from each location
            // "København K" -> "København", "Aarhus C" -> "Aarhus"
            var locationTokens = request?.Locations?
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct()
                .ToList();

            var normalizedSearchTerms = request?.SearchTerms?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct()
                .ToList();

            var normalizedCategoryIds = request?.CategoryIds?
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (_db.Database.IsSqlServer())
            {
                return await BuildRecommendationsSqlServer(keywords, request, locationTokens, normalizedSearchTerms, normalizedCategoryIds, page, pageSize);
            }
            else
            {
                return await BuildRecommendationsInMemory(keywords, request, locationTokens, normalizedSearchTerms, normalizedCategoryIds, page, pageSize);
            }
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendationsSqlServer(
            List<string> keywords,
            RecommendedJobsRequest? request,
            List<string?>? locationTokens,
            List<string>? searchTerms,
            List<int>? categoryIds,
            int page,
            int pageSize)
        {
            var ftQuery = string.Join(" OR ", keywords.Select(k => $"\"{k}\""));
            var off = (page - 1) * pageSize;
            var take = pageSize;

            // Build dynamic WHERE clause for filters
            var whereConditions = new List<string>();
            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@ftQuery", ftQuery),
                new SqlParameter("@off", off),
                new SqlParameter("@take", take)
            };

            if (request?.PostedAfter.HasValue == true)
            {
                whereConditions.Add("j.Published >= @postedAfter");
                parameters.Add(new SqlParameter("@postedAfter", request.PostedAfter.Value));
            }
            if (request?.PostedBefore.HasValue == true)
            {
                whereConditions.Add("j.Published <= @postedBefore");
                parameters.Add(new SqlParameter("@postedBefore", request.PostedBefore.Value));
            }

            // Multiple locations with OR logic
            if (locationTokens != null && locationTokens.Count > 0)
            {
                var locationConditions = new List<string>();
                for (int i = 0; i < locationTokens.Count; i++)
                {
                    var paramName = $"@location{i}";
                    locationConditions.Add($"j.JobLocation LIKE '%' + {paramName} + '%'");
                    parameters.Add(new SqlParameter(paramName, locationTokens[i]));
                }
                whereConditions.Add($"({string.Join(" OR ", locationConditions)})");
            }

            // Multiple categories with OR logic
            if (categoryIds != null && categoryIds.Count > 0)
            {
                var categoryConditions = new List<string>();
                for (int i = 0; i < categoryIds.Count; i++)
                {
                    var paramName = $"@categoryId{i}";
                    categoryConditions.Add($"jc.CategoryID = {paramName}");
                    parameters.Add(new SqlParameter(paramName, categoryIds[i]));
                }
                whereConditions.Add($"EXISTS (SELECT 1 FROM dbo.JobCategories jc WHERE jc.JobID = j.JobID AND ({string.Join(" OR ", categoryConditions)}))");
            }

            // Multiple search terms with OR logic (additional filter on top of profile keywords)
            if (searchTerms != null && searchTerms.Count > 0)
            {
                var searchConditions = new List<string>();
                for (int i = 0; i < searchTerms.Count; i++)
                {
                    var paramName = $"@searchTerm{i}";
                    searchConditions.Add($"(j.JobTitle LIKE '%' + {paramName} + '%' OR j.CompanyName LIKE '%' + {paramName} + '%' OR j.JobDescription LIKE '%' + {paramName} + '%')");
                    parameters.Add(new SqlParameter(paramName, searchTerms[i]));
                }
                whereConditions.Add($"({string.Join(" OR ", searchConditions)})");
            }

            var whereClause = whereConditions.Count > 0
                ? "WHERE " + string.Join(" AND ", whereConditions)
                : "";

            // Use UNION instead of UNION ALL to avoid duplicates, GROUP BY for best rank
            var baseSqlRec = $@"
SELECT j.*
FROM (
    SELECT j.JobID, MAX(r.[RANK]) AS [RANK]
    FROM (
        SELECT t.[KEY] AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, 2000) t
        UNION
        SELECT j.JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, 1000) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ) r
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
    {whereClause}
    GROUP BY j.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
ORDER BY r.[RANK] DESC, j.Published DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";

            var items = await _db.JobIndexPosts
                .FromSqlRaw(baseSqlRec, parameters.ToArray())
                .Include(j => j.Categories)
                .AsNoTracking()
                .ToListAsync();

            // Simplified count query using COUNT(DISTINCT) with Value alias for SqlQueryRaw<int>
            var countSql = $@"
SELECT COUNT(DISTINCT r.JobID) AS Value
FROM (
    SELECT t.[KEY] AS JobID
    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, 2000) t
    UNION
    SELECT j.JobID
    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, 1000) tk
    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
{whereClause}";

            var countParams = parameters.Where(p => p.ParameterName != "@off" && p.ParameterName != "@take")
                .Select(p => new SqlParameter(p.ParameterName, p.Value))
                .ToArray();

            var totalCount = await _db.Database
                .SqlQueryRaw<int>(countSql, countParams)
                .FirstOrDefaultAsync();

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendationsInMemory(
            List<string> keywords,
            RecommendedJobsRequest? request,
            List<string>? locationTokens,
            List<string>? searchTerms,
            List<int>? categoryIds,
            int page,
            int pageSize)
        {
            var kw = keywords.Select(k => k.ToLowerInvariant()).ToList();
            var jobs = await _db.JobIndexPosts.Include(j => j.Categories).AsNoTracking().ToListAsync();
            var jobKeywords = await _db.JobKeywords.AsNoTracking().ToListAsync();

            // First filter by profile keywords (recommendations)
            var filteredJobs = jobs.Where(j =>
                (j.JobTitle != null && kw.Any(k => j.JobTitle!.ToLower().Contains(k))) ||
                (j.CompanyName != null && kw.Any(k => j.CompanyName!.ToLower().Contains(k))) ||
                (j.JobDescription != null && kw.Any(k => j.JobDescription!.ToLower().Contains(k))) ||
                (j.JobLocation != null && kw.Any(k => j.JobLocation!.ToLower().Contains(k))) ||
                (j.Categories.Any(c => c.Name != null && kw.Any(k => c.Name.ToLower().Contains(k)))) ||
                jobKeywords.Any(kj => kj.JobID == j.JobID && kj.Keyword != null && kw.Any(k => kj.Keyword.ToLower().Contains(k)))
            ).AsEnumerable();

            // Apply additional filters from request
            if (request?.PostedAfter.HasValue == true)
            {
                filteredJobs = filteredJobs.Where(j => j.Published >= request.PostedAfter.Value);
            }
            if (request?.PostedBefore.HasValue == true)
            {
                filteredJobs = filteredJobs.Where(j => j.Published <= request.PostedBefore.Value);
            }

            // Multiple locations with OR logic
            if (locationTokens != null && locationTokens.Count > 0)
            {
                filteredJobs = filteredJobs.Where(j =>
                    !string.IsNullOrWhiteSpace(j.JobLocation) &&
                    locationTokens.Any(loc => j.JobLocation!.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Multiple categories with OR logic
            if (categoryIds != null && categoryIds.Count > 0)
            {
                filteredJobs = filteredJobs.Where(j =>
                    j.Categories != null && j.Categories.Any(c => categoryIds.Contains(c.CategoryID)));
            }

            // Multiple search terms with OR logic
            if (searchTerms != null && searchTerms.Count > 0)
            {
                var terms = searchTerms.Select(t => t.ToLowerInvariant()).ToList();
                filteredJobs = filteredJobs.Where(j =>
                    (!string.IsNullOrEmpty(j.JobTitle) && terms.Any(term => j.JobTitle.ToLower().Contains(term))) ||
                    (!string.IsNullOrEmpty(j.CompanyName) && terms.Any(term => j.CompanyName.ToLower().Contains(term))) ||
                    (!string.IsNullOrEmpty(j.JobDescription) && terms.Any(term => j.JobDescription.ToLower().Contains(term))) ||
                    jobKeywords.Any(k => k.JobID == j.JobID && k.Keyword != null && terms.Any(term => k.Keyword.ToLower().Contains(term)))
                );
            }

            var filteredList = filteredJobs.ToList();
            var total = filteredList.Count;

            if (total == 0)
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);

            // Apply pagination AFTER all filters
            var items = filteredList
                .OrderByDescending(j => j.Published)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PagedList<JobIndexPosts>(total, pageSize, page, items);
        }

        private static HashSet<string> GetKeywordsFromProfile(Profile profile)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            profile.Keywords?.Where(kw => !string.IsNullOrWhiteSpace(kw))
                    .ToList()
                    .ForEach(kw => keywords.Add(kw));

            profile.Interests?.Where(i => !string.IsNullOrWhiteSpace(i.Title))
                    .ToList()
                    .ForEach(i => keywords.Add(i.Title));

            profile.Skills?.Where(s => !string.IsNullOrWhiteSpace(s.Name))
                    .ToList()
                    .ForEach(s => keywords.Add(s.Name));

            if (profile.BasicInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.BasicInfo.JobTitle))
                    keywords.Add(profile.BasicInfo.JobTitle);
                if (!string.IsNullOrWhiteSpace(profile.BasicInfo.Company))
                    keywords.Add(profile.BasicInfo.Company);
            }

            if (profile.Experiences != null)
            {
                foreach (var exp in profile.Experiences)
                {
                    if (!string.IsNullOrWhiteSpace(exp.PositionTitle))
                        keywords.Add(exp.PositionTitle);
                    if (!string.IsNullOrWhiteSpace(exp.Company))
                        keywords.Add(exp.Company);
                }
            }

            return keywords;
        }
    }
}
