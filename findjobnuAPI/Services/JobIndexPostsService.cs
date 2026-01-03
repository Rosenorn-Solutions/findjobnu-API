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

        public async Task<PagedList<JobIndexPosts>> SearchAsync(string? searchTerm, string? location, int? categoryId, DateTime? postedAfter, DateTime? postedBefore, int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;

            var normalizedLocation = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
            var locationTokens = normalizedLocation?
                .Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
            var primaryLocationToken = locationTokens?.FirstOrDefault();

            var cacheKey = $"search:{searchTerm}|{primaryLocationToken}|{categoryId}|{postedAfter:O}|{postedBefore:O}|{page}|{pageSize}";
            if (_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var cached) && cached is not null)
            {
                return cached;
            }

            PagedList<JobIndexPosts> result;
            var ftQuery = string.IsNullOrWhiteSpace(searchTerm) ? null : $"\"{searchTerm.Trim()}\"";

            if (_db.Database.IsSqlServer() && !string.IsNullOrWhiteSpace(ftQuery))
            {
                var off = (page - 1) * pageSize;
                var take = pageSize;

                var baseSql = @"
SELECT j.*
FROM (
    SELECT j.JobID, t.[RANK]
    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery) t
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = t.[KEY]
    UNION ALL
    SELECT j.JobID, tk.[RANK]
    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery) tk
    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
WHERE (@postedAfter IS NULL OR j.Published >= @postedAfter)
  AND (@postedBefore IS NULL OR j.Published <= @postedBefore)
  AND (@location IS NULL OR j.JobLocation LIKE '%' + @location + '%')
  AND (@categoryId IS NULL OR EXISTS (
        SELECT 1 FROM dbo.JobCategories jc
        WHERE jc.JobID = j.JobID AND jc.CategoryID = @categoryId
    ))
ORDER BY r.[RANK] DESC, j.Published DESC
OFFSET @off ROWS FETCH NEXT @take ROWS ONLY";

                var itemParams = new object[]
                {
                    new SqlParameter("@ftQuery", ftQuery),
                    new SqlParameter("@postedAfter", (object?)postedAfter ?? DBNull.Value),
                    new SqlParameter("@postedBefore", (object?)postedBefore ?? DBNull.Value),
                    new SqlParameter("@location", (object?)primaryLocationToken ?? DBNull.Value),
                    new SqlParameter("@categoryId", (object?)categoryId ?? DBNull.Value),
                    new SqlParameter("@off", off),
                    new SqlParameter("@take", take)
                };

                var items = await _db.JobIndexPosts
                    .FromSqlRaw(baseSql, itemParams)
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .ToListAsync();

                var countSql = @"
SELECT j.JobID
FROM (
    SELECT j.JobID, t.[RANK]
    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), @ftQuery) t
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = t.[KEY]
    UNION ALL
    SELECT j.JobID, tk.[RANK]
    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery) tk
    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
WHERE (@postedAfter IS NULL OR j.Published >= @postedAfter)
  AND (@postedBefore IS NULL OR j.Published <= @postedBefore)
  AND (@location IS NULL OR j.JobLocation LIKE '%' + @location + '%')
  AND (@categoryId IS NULL OR EXISTS (
        SELECT 1 FROM dbo.JobCategories jc
        WHERE jc.JobID = j.JobID AND jc.CategoryID = @categoryId
    ))";

                var countParams = new object[]
                {
                    new SqlParameter("@ftQuery", ftQuery),
                    new SqlParameter("@postedAfter", (object?)postedAfter ?? DBNull.Value),
                    new SqlParameter("@postedBefore", (object?)postedBefore ?? DBNull.Value),
                    new SqlParameter("@location", (object?)primaryLocationToken ?? DBNull.Value),
                    new SqlParameter("@categoryId", (object?)categoryId ?? DBNull.Value)
                };

                var totalCount = await _db.JobIndexPosts
                    .FromSqlRaw(countSql, countParams)
                    .Select(j => j.JobID)
                    .CountAsync();

                result = new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
            }
            else
            {
                var q = _db.JobIndexPosts.Include(j => j.Categories).AsQueryable();
                if (postedAfter.HasValue) q = q.Where(j => j.Published >= postedAfter.Value);
                if (postedBefore.HasValue) q = q.Where(j => j.Published <= postedBefore.Value);

                if (!string.IsNullOrWhiteSpace(primaryLocationToken))
                {
                    var locationToken = primaryLocationToken;
                    q = q.Where(j => j.JobLocation != null && j.JobLocation.Contains(locationToken));
                }
                if (categoryId.HasValue)
                {
                    q = q.Where(j => j.Categories.Any(c => c.CategoryID == categoryId.Value));
                }
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim().ToLowerInvariant();
                    q = q.Where(j =>
                        (j.JobTitle != null && j.JobTitle.ToLower().Contains(term)) ||
                        (j.CompanyName != null && j.CompanyName.ToLower().Contains(term)) ||
                        (j.JobDescription != null && j.JobDescription.ToLower().Contains(term)) ||
                        _db.JobKeywords.Any(k => k.JobID == j.JobID && k.Keyword != null && k.Keyword.ToLower().Contains(term))
                    );
                }

                var total = await q.CountAsync();
                if (total == 0)
                {
                    result = new PagedList<JobIndexPosts>(0, pageSize, page, []);
                }
                else
                {
                    var items = await q
                        .OrderByDescending(j => j.Published)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .AsNoTracking()
                        .ToListAsync();

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
                    NumberOfJobs = c.JobIndexPosts.Count()
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

            var cacheKey = $"rec:{userId}:{page}:{pageSize}";
            if (!_cache.TryGetValue<PagedList<JobIndexPosts>>(cacheKey, out var baseResult) || baseResult == null)
            {
                baseResult = await BuildRecommendations(userId, page, pageSize);
                _cache.Set(cacheKey, baseResult, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
                });
            }

            return ApplyRecommendationFilters(baseResult, request, page, pageSize);
        }

        private async Task<PagedList<JobIndexPosts>> BuildRecommendations(string userId, int page, int pageSize)
        {
            PagedList<JobIndexPosts> result;

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

            if (_db.Database.IsSqlServer())
            {
                var ftQuery = string.Join(" OR ", keywords.Select(k => $"\"{k}\""));
                var off = (page - 1) * pageSize;
                var take = pageSize;

                var baseSqlRec = @"
SELECT j.*
FROM (
    SELECT j.JobID, t.[RANK]
    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), {0}) t
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = t.[KEY]
    UNION ALL
    SELECT j.JobID, tk.[RANK]
    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, {0}) tk
    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
) r
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = r.JobID
ORDER BY r.[RANK] DESC, j.Published DESC
OFFSET {1} ROWS FETCH NEXT {2} ROWS ONLY";

                var items = await _db.JobIndexPosts
                    .FromSqlRaw(baseSqlRec, ftQuery, off, take)
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .ToListAsync();

                var countSqlRec = @"
SELECT j.JobID
FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, (JobTitle, JobDescription, CompanyName, JobLocation), {0}) t
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = t.[KEY]
UNION
SELECT j.JobID
FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, {0}) tk
JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID";

                var totalCount = await _db.JobIndexPosts
                    .FromSqlRaw(countSqlRec, ftQuery)
                    .Select(j => j.JobID)
                    .CountAsync();

                result = new PagedList<JobIndexPosts>(totalCount, pageSize, page, items);
            }
            else
            {
                var kw = keywords.Select(k => k.ToLowerInvariant()).ToList();
                var jobs = await _db.JobIndexPosts.Include(j => j.Categories).AsNoTracking().ToListAsync();
                var jobKeywords = await _db.JobKeywords.AsNoTracking().ToListAsync();

                var filteredJobs = jobs.Where(j =>
                    (j.JobTitle != null && kw.Any(k => j.JobTitle!.ToLower().Contains(k))) ||
                    (j.CompanyName != null && kw.Any(k => j.CompanyName!.ToLower().Contains(k))) ||
                    (j.JobDescription != null && kw.Any(k => j.JobDescription!.ToLower().Contains(k))) ||
                    (j.JobLocation != null && kw.Any(k => j.JobLocation!.ToLower().Contains(k))) ||
                    (j.Categories.Any(c => c.Name != null && kw.Any(k => c.Name.ToLower().Contains(k)))) ||
                    jobKeywords.Any(kj => kj.JobID == j.JobID && kj.Keyword != null && kw.Any(k => kj.Keyword.ToLower().Contains(k)))
                ).ToList();

                var total = filteredJobs.Count;
                if (total == 0) return new PagedList<JobIndexPosts>(0, pageSize, page, []);

                var items = filteredJobs
                    .OrderByDescending(j => j.Published)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                result = new PagedList<JobIndexPosts>(total, pageSize, page, items);
            }

            return result;
        }

        private PagedList<JobIndexPosts> ApplyRecommendationFilters(PagedList<JobIndexPosts> baseResult, RecommendedJobsRequest? request, int page, int pageSize)
        {
            if (request == null) return baseResult;

            var filtered = baseResult.Items ?? Enumerable.Empty<JobIndexPosts>();

            var normalizedLocation = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
            var locationTokens = normalizedLocation?
                .Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
            var primaryLocationToken = locationTokens?.FirstOrDefault();

            if (request.PostedAfter.HasValue)
            {
                filtered = filtered.Where(j => j.Published >= request.PostedAfter.Value);
            }
            if (request.PostedBefore.HasValue)
            {
                filtered = filtered.Where(j => j.Published <= request.PostedBefore.Value);
            }
            if (!string.IsNullOrWhiteSpace(primaryLocationToken))
            {
                var locationToken = primaryLocationToken;
                filtered = filtered.Where(j => !string.IsNullOrWhiteSpace(j.JobLocation) && j.JobLocation!.IndexOf(locationToken, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (request.CategoryId.HasValue)
            {
                var categoryId = request.CategoryId.Value;
                filtered = filtered.Where(j => j.Categories != null && j.Categories.Any(c => c.CategoryID == categoryId));
            }
            if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                var term = request.SearchTerm.Trim().ToLowerInvariant();
                filtered = filtered.Where(j =>
                    (!string.IsNullOrEmpty(j.JobTitle) && j.JobTitle.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(j.CompanyName) && j.CompanyName.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(j.JobDescription) && j.JobDescription.ToLower().Contains(term)) ||
                    _db.JobKeywords.Any(k => k.JobID == j.JobID && k.Keyword != null && k.Keyword.ToLower().Contains(term))
                );
            }

            var filteredList = filtered.ToList();
            return new PagedList<JobIndexPosts>(filteredList.Count, pageSize, page, filteredList);
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