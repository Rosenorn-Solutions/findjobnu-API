using FindjobnuService.DTOs.Requests;
using FindjobnuService.DTOs.Responses;
using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FindjobnuService.Services
{
    /// <summary>
    /// Service for job search and recommendations.
    /// 
    /// PERFORMANCE NOTES:
    /// -----------------
    /// For optimal SQL Server performance, ensure the following are configured:
    /// 
    /// 1. Run the SQL scripts in order:
    ///    - CREATE_FULLTEXT_Jobs.sql (full-text catalog and indexes)
    ///    - CREATE_SEARCH_INDEXES.sql (supporting indexes for filters)
    ///    - CREATE_PROC_SearchJobs.sql (search stored procedure)
    ///    - CREATE_PROC_RecommendedJobs.sql (recommendations stored procedure)
    /// 
    /// 2. Required indexes:
    ///    - IX_JobIndexPostingsExtended_JobLocation (location LIKE queries)
    ///    - IX_JobIndexPostingsExtended_Published (date range queries)
    ///    - IX_JobCategories_JobID_CategoryID (category filtering)
    ///    - IX_JobCategories_CategoryID_JobID (reverse lookup)
    ///    - Full-text indexes on JobIndexPostingsExtended and JobKeywords
    /// 
    /// 3. To use stored procedures (recommended for production):
    ///    Set UseStoredProcedures = true after deploying the SQL scripts.
    /// </summary>
    public class JobIndexPostsService : IJobIndexPostsService
    {
        private const int DefaultRecommendationFreshnessDays = 60;
        public int RecommendationMinRank { get; set; } = 50;
        private readonly FindjobnuContext _db;
        private readonly ILogger<JobIndexPostsService> _logger;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Set to true to use stored procedures instead of inline SQL.
        /// Stored procedures provide query plan caching benefits.
        /// Requires: CREATE_PROC_SearchJobs.sql and CREATE_PROC_RecommendedJobs.sql
        /// </summary>
        public bool UseStoredProcedures { get; set; } = false;

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

            // Use hash-based cache key for efficiency
            var cacheKey = JobSearchQueryBuilder.GenerateCacheKey(
                "search",
                normalizedSearchTerms,
                locationTokens,
                normalizedCategoryIds,
                postedAfter,
                postedBefore,
                page,
                pageSize);

            if (_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            PagedList<JobIndexPosts> result;

            bool hasSearchTerms = normalizedSearchTerms != null && normalizedSearchTerms.Count > 0;
            bool hasFilters = (locationTokens != null && locationTokens.Count > 0) ||
                              (normalizedCategoryIds != null && normalizedCategoryIds.Count > 0) ||
                              postedAfter.HasValue || postedBefore.HasValue;

            if (_db.Database.IsSqlServer())
            {
                if (hasSearchTerms)
                {
                    // Full-text search with optional filters
                    result = await ExecuteSqlServerSearchAsync(
                        normalizedSearchTerms!,
                        locationTokens,
                        normalizedCategoryIds,
                        postedAfter,
                        postedBefore,
                        page,
                        pageSize);
                }
                else if (hasFilters)
                {
                    // Filter-only search (no full-text) - use EF Core query
                    result = await ExecuteSqlServerFilterOnlyAsync(
                        locationTokens,
                        normalizedCategoryIds,
                        postedAfter,
                        postedBefore,
                        page,
                        pageSize);
                }
                else
                {
                    // No search terms, no filters - just return paginated results
                    result = await GetAllAsync(page, pageSize);
                }
            }
            else
            {
                // InMemory provider (tests)
                result = await ExecuteInMemorySearchAsync(
                    normalizedSearchTerms,
                    locationTokens,
                    normalizedCategoryIds,
                    postedAfter,
                    postedBefore,
                    page,
                    pageSize);
            }

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });

            return result;
        }

        /// <summary>
        /// Executes filter-only search using EF Core (no full-text search).
        /// Used when location, category, or date filters are provided but no search terms.
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> ExecuteSqlServerFilterOnlyAsync(
            List<string?>? locationTokens,
            List<int>? categoryIds,
            DateTime? postedAfter,
            DateTime? postedBefore,
            int page,
            int pageSize)
        {
            var query = _db.JobIndexPosts
                .Include(j => j.Categories)
                .AsNoTracking()
                .AsQueryable();

            // Apply date filters
            if (postedAfter.HasValue)
            {
                query = query.Where(j => j.Published >= postedAfter.Value);
            }
            if (postedBefore.HasValue)
            {
                query = query.Where(j => j.Published <= postedBefore.Value);
            }

            // Apply location filter (OR logic)
            if (locationTokens != null && locationTokens.Count > 0)
            {
                // Build OR condition for multiple locations
                query = query.Where(j =>
                    j.JobLocation != null &&
                    locationTokens.Any(loc => j.JobLocation.Contains(loc!)));
            }

            // Apply category filter (OR logic)
            if (categoryIds != null && categoryIds.Count > 0)
            {
                query = query.Where(j =>
                    j.Categories.Any(c => categoryIds.Contains(c.CategoryID)));
            }

            // Get total count
            var totalCount = await query.CountAsync();

            if (totalCount == 0)
            {
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);
            }

            // Get paginated results
            var items = await query
                .OrderByDescending(j => j.Published)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        /// <summary>
        /// Executes optimized SQL Server search using full-text CONTAINSTABLE.
        /// Can use either inline SQL or stored procedure based on UseStoredProcedures setting.
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> ExecuteSqlServerSearchAsync(
            List<string> searchTerms,
            List<string?>? locationTokens,
            List<int>? categoryIds,
            DateTime? postedAfter,
            DateTime? postedBefore,
            int page,
            int pageSize)
        {
            var queryBuilder = new JobSearchQueryBuilder()
                .WithFullTextQuery(searchTerms)
                .WithPagination(page, pageSize)
                .WithDateRange(postedAfter, postedBefore)
                .WithLocations(locationTokens)
                .WithCategories(categoryIds);

            if (UseStoredProcedures)
            {
                return await ExecuteSearchStoredProcedureAsync(queryBuilder, page, pageSize);
            }

            var sql = queryBuilder.BuildSearchSqlWithCount();
            var parameters = queryBuilder.GetParameters();

            // Execute single query that returns both data and count
            var rawResults = await _db.JobIndexPosts
                .FromSqlRaw(sql, parameters)
                .Include(j => j.Categories)
                .AsNoTracking()
                .ToListAsync();

            // Execute count query only if we have results
            int totalCount = 0;
            if (rawResults.Count > 0)
            {
                totalCount = await ExecuteCountQueryAsync(queryBuilder);
            }

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, rawResults);
        }

        /// <summary>
        /// Executes search using stored procedure for query plan caching benefits.
        /// Note: Categories are loaded separately since stored procedures are non-composable.
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> ExecuteSearchStoredProcedureAsync(
            JobSearchQueryBuilder queryBuilder,
            int page,
            int pageSize)
        {
            var parameters = queryBuilder.GetStoredProcedureParameters(includeSearchTerms: false);
            var sql = queryBuilder.BuildSearchStoredProcedureCall();

            // Execute stored procedure - cannot use Include() with stored procedures
            var items = await _db.JobIndexPosts
                .FromSqlRaw(sql, parameters)
                .AsNoTracking()
                .ToListAsync();

            // Load categories separately for the returned jobs
            if (items.Count > 0)
            {
                var jobIds = items.Select(j => j.JobID).ToList();
                var jobCategories = await _db.JobIndexPosts
                    .Where(j => jobIds.Contains(j.JobID))
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .ToDictionaryAsync(j => j.JobID, j => j.Categories);

                foreach (var job in items)
                {
                    if (jobCategories.TryGetValue(job.JobID, out var categories))
                    {
                        job.Categories = categories;
                    }
                }
            }

            // Get total count from output parameter
            var totalCountParam = parameters.FirstOrDefault(p => p.ParameterName == "@totalCount");
            var totalCount = totalCountParam?.Value is int count ? count : 0;

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        /// <summary>
        /// Executes a lightweight count query using the same filters.
        /// </summary>
        private async Task<int> ExecuteCountQueryAsync(JobSearchQueryBuilder queryBuilder)
        {
            var whereClause = queryBuilder.BuildWhereClause();
            var countParams = queryBuilder.GetCountParameters();

            var countSql = $@"
SELECT COUNT(*) AS Value
FROM (
    SELECT r.JobID
    FROM (
        SELECT t.[KEY] AS JobID
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, {queryBuilder.MainTableTopN}) t
        UNION
        SELECT j.JobID
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, {queryBuilder.KeywordsTableTopN}) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ) r
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
    {whereClause}
    GROUP BY r.JobID
) counted";

            return await _db.Database
                .SqlQueryRaw<int>(countSql, countParams)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Executes in-memory search for InMemory provider (tests only).
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> ExecuteInMemorySearchAsync(
            List<string>? searchTerms,
            List<string?>? locationTokens,
            List<int>? categoryIds,
            DateTime? postedAfter,
            DateTime? postedBefore,
            int page,
            int pageSize)
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
                return new PagedList<JobIndexPosts>(0, pageSize, page, []);
            }

            var items = filteredList
                .OrderByDescending(j => j.Published)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PagedList<JobIndexPosts>(total, pageSize, page, items);
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

            var effectivePostedAfter = request?.PostedAfter ?? DateTime.UtcNow.AddDays(-DefaultRecommendationFreshnessDays);

            // Normalize filter parameters for cache key
            var searchTerms = request?.SearchTerms?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var locations = request?.Locations?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var categoryIds = request?.CategoryIds?.Where(id => id > 0).ToList();

            // Use hash-based cache key for efficiency
            var cacheKey = JobSearchQueryBuilder.GenerateCacheKey(
                $"rec:{userId}:m{RecommendationMinRank}:fa{effectivePostedAfter.Ticks}",
                searchTerms,
                locations,
                categoryIds,
                effectivePostedAfter,
                request?.PostedBefore,
                page,
                pageSize);

            if (_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            // Build recommendations with filters applied before paging
            var result = await BuildRecommendations(userId, request, page, pageSize, effectivePostedAfter);

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });

            return result;
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendations(string userId, RecommendedJobsRequest? request, int page, int pageSize, DateTime? effectivePostedAfter)
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
                return await BuildRecommendationsSqlServer(keywords, request, locationTokens, normalizedSearchTerms, normalizedCategoryIds, page, pageSize, effectivePostedAfter);
            }
            else
            {
                return await BuildRecommendationsInMemory(keywords, request, locationTokens, normalizedSearchTerms, normalizedCategoryIds, page, pageSize, effectivePostedAfter);
            }
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendationsSqlServer(
            List<string> keywords,
            RecommendedJobsRequest? request,
            List<string?>? locationTokens,
            List<string>? searchTerms,
            List<int>? categoryIds,
            int page,
            int pageSize,
            DateTime? effectivePostedAfter)
        {
            var queryBuilder = new JobSearchQueryBuilder()
                .WithFullTextQuery(keywords)
                .WithPagination(page, pageSize)
                .WithDateRange(effectivePostedAfter, request?.PostedBefore)
                .WithLocations(locationTokens)
                .WithCategories(categoryIds)
                .WithSearchTermsLike(searchTerms)
                .WithMinRank(RecommendationMinRank);

            if (UseStoredProcedures)
            {
                return await ExecuteRecommendationsStoredProcedureAsync(queryBuilder, page, pageSize);
            }

            var sql = queryBuilder.BuildRecommendationsSqlWithCount();
            var parameters = queryBuilder.GetParameters();

            var items = await _db.JobIndexPosts
                .FromSqlRaw(sql, parameters)
                .Include(j => j.Categories)
                .AsNoTracking()
                .ToListAsync();

            // Execute count query only if we have results
            int totalCount = 0;
            if (items.Count > 0)
            {
                totalCount = await ExecuteRecommendationsCountQueryAsync(queryBuilder);
            }

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        /// <summary>
        /// Executes recommendations using stored procedure for query plan caching benefits.
        /// Note: Categories are loaded separately since stored procedures are non-composable.
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> ExecuteRecommendationsStoredProcedureAsync(
            JobSearchQueryBuilder queryBuilder,
            int page,
            int pageSize)
        {
            var parameters = queryBuilder.GetStoredProcedureParameters(includeSearchTerms: true);
            var sql = queryBuilder.BuildRecommendationsStoredProcedureCall();

            // Execute stored procedure - cannot use Include() with stored procedures
            var items = await _db.JobIndexPosts
                .FromSqlRaw(sql, parameters)
                .AsNoTracking()
                .ToListAsync();

            // Load categories separately for the returned jobs
            if (items.Count > 0)
            {
                var jobIds = items.Select(j => j.JobID).ToList();
                var jobCategories = await _db.JobIndexPosts
                    .Where(j => jobIds.Contains(j.JobID))
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .ToDictionaryAsync(j => j.JobID, j => j.Categories);

                foreach (var job in items)
                {
                    if (jobCategories.TryGetValue(job.JobID, out var categories))
                    {
                        job.Categories = categories;
                    }
                }
            }

            // Get total count from output parameter
            var totalCountParam = parameters.FirstOrDefault(p => p.ParameterName == "@totalCount");
            var totalCount = totalCountParam?.Value is int count ? count : 0;

            return new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
        }

        /// <summary>
        /// Executes count query for recommendations.
        /// </summary>
        private async Task<int> ExecuteRecommendationsCountQueryAsync(JobSearchQueryBuilder queryBuilder)
        {
            var whereClause = queryBuilder.BuildWhereClause();
            var countParams = queryBuilder.GetCountParameters();

            var countSql = $@"
SELECT COUNT(DISTINCT r.JobID) AS Value
FROM (
    SELECT t.[KEY] AS JobID
    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery, {queryBuilder.MainTableTopN}) t
    UNION
    SELECT j.JobID
    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, {queryBuilder.KeywordsTableTopN}) tk
    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
{whereClause}";

            return await _db.Database
                .SqlQueryRaw<int>(countSql, countParams)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Builds recommendations in-memory for InMemory provider (tests only).
        /// </summary>
        private async Task<PagedList<JobIndexPosts>> BuildRecommendationsInMemory(
            List<string> keywords,
            RecommendedJobsRequest? request,
            List<string?>? locationTokens,
            List<string>? searchTerms,
            List<int>? categoryIds,
            int page,
            int pageSize,
            DateTime? effectivePostedAfter)
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
            if (effectivePostedAfter.HasValue)
            {
                filteredJobs = filteredJobs.Where(j => j.Published >= effectivePostedAfter.Value);
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
                    locationTokens.Any(loc => j.JobLocation!.IndexOf(loc!, StringComparison.OrdinalIgnoreCase) >= 0));
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
