using FindjobnuService.Models;

namespace FindjobnuService.Services
{
    public interface IJobAgentService
    {
        Task<JobAgent> CreateOrUpdateAsync(
            int profileId,
            bool enabled,
            JobAgentFrequency frequency,
            IEnumerable<string>? preferredLocations,
            IEnumerable<int>? preferredCategoryIds,
            IEnumerable<string>? includeKeywords);
        Task<string?> GetOrCreateUnsubscribeTokenAsync(int profileId);
        Task<bool> UnsubscribeByTokenAsync(string token);
        Task<JobAgent?> GetByProfileIdAsync(int profileId);
    }
}
