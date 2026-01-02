using FindjobnuService.Models;
using FindjobnuService.Repositories.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace FindjobnuService.Services
{
    public class JobAgentService : IJobAgentService
    {
        private readonly FindjobnuContext _db;
        private readonly ILogger<JobAgentService> _logger;

        public JobAgentService(FindjobnuContext db, ILogger<JobAgentService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // Adjust signature to match interface (non-nullable frequency)
        public async Task<JobAgent> CreateOrUpdateAsync(
            int profileId,
            bool enabled,
            JobAgentFrequency frequency,
            IEnumerable<string>? preferredLocations,
            IEnumerable<int>? preferredCategoryIds,
            IEnumerable<string>? includeKeywords)
        {
            var profile = await _db.Profiles.Include(p => p.JobAgent).FirstOrDefaultAsync(p => p.Id == profileId);
            if (profile == null) throw new InvalidOperationException("Profile not found");

            var now = DateTime.UtcNow;
            if (profile.JobAgent == null)
            {
                profile.JobAgent = new JobAgent
                {
                    ProfileId = profileId,
                    Enabled = enabled,
                    Frequency = frequency,
                    CreatedAt = now,
                    NextSendAt = ComputeNext(now, frequency),
                    UnsubscribeToken = GenerateToken(),
                    PreferredLocations = NormalizeStrings(preferredLocations),
                    PreferredCategoryIds = NormalizeInts(preferredCategoryIds),
                    IncludeKeywords = NormalizeStrings(includeKeywords)
                };
                _db.JobAgents.Add(profile.JobAgent);
            }
            else
            {
                profile.JobAgent.Enabled = enabled;
                profile.JobAgent.Frequency = frequency;
                profile.JobAgent.UpdatedAt = now;
                profile.JobAgent.PreferredLocations = NormalizeStrings(preferredLocations);
                profile.JobAgent.PreferredCategoryIds = NormalizeInts(preferredCategoryIds);
                profile.JobAgent.IncludeKeywords = NormalizeStrings(includeKeywords);
                if (enabled && profile.JobAgent.NextSendAt == null)
                {
                    profile.JobAgent.NextSendAt = ComputeNext(now, profile.JobAgent.Frequency);
                }
                if (string.IsNullOrEmpty(profile.JobAgent.UnsubscribeToken))
                {
                    profile.JobAgent.UnsubscribeToken = GenerateToken();
                }
            }

            profile.HasJobAgent = enabled;
            await _db.SaveChangesAsync();
            return profile.JobAgent!;
        }

        public async Task<JobAgent?> GetByProfileIdAsync(int profileId)
        {
            return await _db.JobAgents.AsNoTracking().FirstOrDefaultAsync(x => x.ProfileId == profileId);
        }

        public async Task<string?> GetOrCreateUnsubscribeTokenAsync(int profileId)
        {
            var agent = await _db.JobAgents.FirstOrDefaultAsync(x => x.ProfileId == profileId);
            if (agent == null) return null;
            if (string.IsNullOrEmpty(agent.UnsubscribeToken))
            {
                agent.UnsubscribeToken = GenerateToken();
                await _db.SaveChangesAsync();
            }
            return agent.UnsubscribeToken;
        }

        public async Task<bool> UnsubscribeByTokenAsync(string token)
        {
            var agent = await _db.JobAgents.FirstOrDefaultAsync(x => x.UnsubscribeToken == token);
            if (agent == null) return false;
            agent.Enabled = false;
            agent.NextSendAt = null;
            agent.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        private static DateTime ComputeNext(DateTime from, JobAgentFrequency freq)
        {
            return freq switch
            {
                JobAgentFrequency.Daily => from.AddDays(1),
                JobAgentFrequency.Monthly => from.AddMonths(1),
                _ => from.AddDays(7)
            };
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static List<string> NormalizeStrings(IEnumerable<string>? values)
        {
            if (values == null) return new List<string>();
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Where(v => v.Length <= 200)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<int> NormalizeInts(IEnumerable<int>? values)
        {
            if (values == null) return new List<int>();
            return values
                .Where(v => v > 0)
                .Distinct()
                .ToList();
        }
    }
}
