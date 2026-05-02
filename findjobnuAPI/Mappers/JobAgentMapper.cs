using FindjobnuService.DTOs;
using FindjobnuService.Models;

namespace FindjobnuService.Mappers
{
    public static class JobAgentMapper
    {
        public static JobAgentDto? ToDto(JobAgent? agent, IEnumerable<Category>? categories = null)
        {
            if (agent == null)
                return null;

            var categoryNames = new List<string>();
            if (categories != null && agent.PreferredCategoryKeys != null)
            {
                var categoryDict = categories.ToDictionary(c => c.CategoryKey, c => c.CategoryName, StringComparer.OrdinalIgnoreCase);
                categoryNames = agent.PreferredCategoryKeys
                    .Where(key => categoryDict.ContainsKey(key))
                    .Select(key => categoryDict[key])
                    .ToList();
            }

            return new JobAgentDto
            {
                Id = agent.Id,
                ProfileId = agent.ProfileId,
                Enabled = agent.Enabled,
                Frequency = agent.Frequency,
                LastSentAt = agent.LastSentAt,
                NextSendAt = agent.NextSendAt,
                CreatedAt = agent.CreatedAt,
                UpdatedAt = agent.UpdatedAt,
                PreferredLocations = agent.PreferredLocations ?? new List<string>(),
                PreferredCategoryKeys = agent.PreferredCategoryKeys ?? new List<string>(),
                PreferredCategoryNames = categoryNames,
                IncludeKeywords = agent.IncludeKeywords ?? new List<string>()
            };
        }
    }
}
