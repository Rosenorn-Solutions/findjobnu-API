using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FindjobnuService.Services
{
    /// <summary>
    /// ML-based recommendation service using:
    /// 1. Content-Based Filtering: Match jobs to user profile features
    /// 2. Collaborative Filtering: Find similar users and recommend their jobs
    /// 3. Implicit Feedback: Learn from user interactions (views, clicks, saves, applies)
    /// </summary>
    public class MLRecommendationService : IMLRecommendationService
    {
        private readonly FindjobnuContext _db;
        private readonly ILogger<MLRecommendationService> _logger;
        private readonly IMemoryCache _cache;

        public MLRecommendationService(
            FindjobnuContext db,
            ILogger<MLRecommendationService> logger,
            IMemoryCache cache)
        {
            _db = db;
            _logger = logger;
            _cache = cache;
        }

        public async Task<List<(int JobId, double Score)>> GetRecommendationsAsync(string userId, Profile profile, int topN = 100)
        {
            try
            {
                var cacheKey = $"ml_rec:{userId}:{topN}";
                if (_cache.TryGetValue<List<(int, double)>>(cacheKey, out var cached) && cached != null)
                {
                    return cached;
                }

                // Get all active jobs
                var allJobs = await _db.JobIndexPosts
                    .Include(j => j.Categories)
                    .AsNoTracking()
                    .Where(j => j.Published >= DateTime.UtcNow.AddMonths(-3)) // Only recent jobs
                    .ToListAsync();

                if (allJobs.Count == 0)
                    return new List<(int, double)>();

                // Get user's interaction history
                var userInteractions = await _db.UserJobInteractions
                    .Where(i => i.UserId == userId)
                    .AsNoTracking()
                    .ToListAsync();

                var jobKeywords = await _db.JobKeywords
                    .AsNoTracking()
                    .ToListAsync();

                // Calculate scores for each job
                var scoredJobs = new List<(int JobId, double Score)>();

                foreach (var job in allJobs)
                {
                    double score = 0;

                    // 1. Content-Based Score (profile match)
                    var contentScore = CalculateContentBasedScore(profile, job, jobKeywords);
                    score += contentScore * 0.4; // 40% weight

                    // 2. Collaborative Filtering Score (similar users)
                    var collaborativeScore = await CalculateCollaborativeScore(userId, job.JobID, userInteractions);
                    score += collaborativeScore * 0.3; // 30% weight

                    // 3. Implicit Feedback Score (user's past behavior)
                    var feedbackScore = CalculateImplicitFeedbackScore(job.JobID, userInteractions);
                    score += feedbackScore * 0.2; // 20% weight

                    // 4. Recency Boost (newer jobs get slight boost)
                    var recencyScore = CalculateRecencyScore(job.Published);
                    score += recencyScore * 0.1; // 10% weight

                    // Don't recommend jobs the user already applied to or unsaved
                    if (userInteractions.Any(i => i.JobId == job.JobID && 
                        (i.InteractionType == InteractionType.Apply || i.InteractionType == InteractionType.Unsave)))
                    {
                        score *= 0.1; // Heavily penalize
                    }

                    scoredJobs.Add((job.JobID, score));
                }

                // Sort by score and take top N
                var recommendations = scoredJobs
                    .OrderByDescending(x => x.Score)
                    .Take(topN)
                    .ToList();

                // Cache for 30 seconds
                _cache.Set(cacheKey, recommendations, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                });

                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ML recommendations for user {UserId}", userId);
                return new List<(int, double)>();
            }
        }

        public async Task RecordInteractionAsync(string userId, int jobId, InteractionType interactionType, int? durationSeconds = null)
        {
            try
            {
                var score = interactionType switch
                {
                    InteractionType.View => 1,
                    InteractionType.Click => 2,
                    InteractionType.Save => 3,
                    InteractionType.Apply => 5,
                    InteractionType.Unsave => -3,
                    _ => 0
                };

                var interaction = new UserJobInteraction
                {
                    UserId = userId,
                    JobId = jobId,
                    InteractionType = interactionType,
                    InteractionDate = DateTime.UtcNow,
                    DurationSeconds = durationSeconds,
                    Score = score
                };

                _db.UserJobInteractions.Add(interaction);
                await _db.SaveChangesAsync();

                // Invalidate cache for this user
                _cache.Remove($"ml_rec:{userId}:100");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording interaction for user {UserId}, job {JobId}", userId, jobId);
            }
        }

        public Task UpdateModelAsync()
        {
            // This would be called periodically (e.g., nightly) to:
            // 1. Clean up old interactions
            // 2. Recalculate user similarity matrices
            // 3. Update category/skill weights based on successful placements
            // For now, it's a placeholder for future ML model training
            _logger.LogInformation("ML model update triggered");
            return Task.CompletedTask;
        }

        #region Scoring Algorithms

        private double CalculateContentBasedScore(Profile profile, JobIndexPosts job, List<JobKeyword> allJobKeywords)
        {
            double score = 0;
            var profileKeywords = GetKeywordsFromProfile(profile)
                .Select(k => k.ToLowerInvariant())
                .ToHashSet();

            if (profileKeywords.Count == 0)
                return 0;

            var jobText = $"{job.JobTitle} {job.JobDescription} {job.CompanyName} {job.JobLocation}".ToLowerInvariant();
            var jobKeywords = allJobKeywords
                .Where(jk => jk.JobID == job.JobID && !string.IsNullOrWhiteSpace(jk.Keyword))
                .Select(jk => jk.Keyword!.ToLowerInvariant())
                .ToList();

            // Exact keyword matches
            foreach (var keyword in profileKeywords)
            {
                if (jobText.Contains(keyword))
                    score += 2.0;
                
                if (jobKeywords.Any(jk => jk.Contains(keyword)))
                    score += 1.5;
            }

            // Category match bonus
            if (job.Categories?.Any() == true && profile.Interests?.Any() == true)
            {
                var profileInterestKeywords = profile.Interests
                    .Select(i => i.Title.ToLowerInvariant())
                    .ToHashSet();

                foreach (var category in job.Categories)
                {
                    if (profileInterestKeywords.Any(i => category.Name?.ToLowerInvariant().Contains(i) == true))
                        score += 3.0;
                }
            }

            // Location match bonus
            if (!string.IsNullOrWhiteSpace(job.JobLocation) && 
                !string.IsNullOrWhiteSpace(profile.BasicInfo?.Location))
            {
                var jobLocation = job.JobLocation.ToLowerInvariant();
                var profileLocation = profile.BasicInfo.Location.ToLowerInvariant();
                
                if (jobLocation.Contains(profileLocation) || profileLocation.Contains(jobLocation))
                    score += 2.0;
            }

            // Skill proficiency weighting
            if (profile.Skills?.Any() == true)
            {
                foreach (var skill in profile.Skills)
                {
                    if (jobText.Contains(skill.Name.ToLowerInvariant()))
                    {
                        score += skill.Proficiency switch
                        {
                            SkillProficiency.Expert => 4.0,
                            SkillProficiency.Advanced => 3.0,
                            SkillProficiency.Intermediate => 2.0,
                            SkillProficiency.Beginner => 1.0,
                            _ => 0
                        };
                    }
                }
            }

            return score;
        }

        private async Task<double> CalculateCollaborativeScore(string userId, int jobId, List<UserJobInteraction> userInteractions)
        {
            try
            {
                // Find users with similar interaction patterns
                var userJobIds = userInteractions
                    .Where(i => i.Score > 0) // Only positive interactions
                    .Select(i => i.JobId)
                    .Distinct()
                    .ToHashSet();

                if (userJobIds.Count == 0)
                    return 0;

                // Find other users who interacted with same jobs
                var similarUsers = await _db.UserJobInteractions
                    .Where(i => i.UserId != userId && userJobIds.Contains(i.JobId))
                    .GroupBy(i => i.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        CommonJobs = g.Select(x => x.JobId).Distinct().Count(),
                        AvgScore = g.Average(x => x.Score)
                    })
                    .Where(x => x.CommonJobs >= 2) // At least 2 common jobs
                    .OrderByDescending(x => x.CommonJobs)
                    .Take(10)
                    .ToListAsync();

                if (similarUsers.Count == 0)
                    return 0;

                // Check if similar users interacted positively with this job
                var similarUserIds = similarUsers.Select(u => u.UserId).ToList();
                var jobInteractionsBySimilar = await _db.UserJobInteractions
                    .Where(i => similarUserIds.Contains(i.UserId) && i.JobId == jobId && i.Score > 0)
                    .ToListAsync();

                if (jobInteractionsBySimilar.Count == 0)
                    return 0;

                // Weight by similarity strength and interaction score
                double score = 0;
                foreach (var interaction in jobInteractionsBySimilar)
                {
                    var similarUser = similarUsers.First(u => u.UserId == interaction.UserId);
                    var similarityWeight = (double)similarUser.CommonJobs / userJobIds.Count;
                    score += interaction.Score * similarityWeight;
                }

                return score / jobInteractionsBySimilar.Count;
            }
            catch
            {
                return 0;
            }
        }

        private double CalculateImplicitFeedbackScore(int jobId, List<UserJobInteraction> userInteractions)
        {
            var jobInteractions = userInteractions
                .Where(i => i.JobId == jobId)
                .ToList();

            if (jobInteractions.Count == 0)
                return 0;

            // Sum up interaction scores with recency decay
            double score = 0;
            var now = DateTime.UtcNow;

            foreach (var interaction in jobInteractions)
            {
                var daysSince = (now - interaction.InteractionDate).TotalDays;
                var recencyFactor = Math.Exp(-daysSince / 30.0); // Exponential decay over 30 days
                score += interaction.Score * recencyFactor;

                // Duration bonus for views
                if (interaction.InteractionType == InteractionType.View && interaction.DurationSeconds.HasValue)
                {
                    // Long views indicate high interest
                    if (interaction.DurationSeconds > 60)
                        score += 1.0;
                }
            }

            return score;
        }

        private double CalculateRecencyScore(DateTime? published)
        {
            if (!published.HasValue)
                return 0;

            var daysSincePublished = (DateTime.UtcNow - published.Value).TotalDays;
            
            // Jobs less than 7 days old get full score
            if (daysSincePublished <= 7)
                return 1.0;
            
            // Jobs 7-30 days old get declining score
            if (daysSincePublished <= 30)
                return 1.0 - ((daysSincePublished - 7) / 23 * 0.5);
            
            // Jobs 30-90 days old get minimal score
            if (daysSincePublished <= 90)
                return 0.5 - ((daysSincePublished - 30) / 60 * 0.5);
            
            return 0;
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

        #endregion
    }
}
