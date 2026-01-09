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
            if (categories != null && agent.PreferredCategoryIds != null)
            {
                var categoryDict = categories.ToDictionary(c => c.CategoryID, c => c.Name);
                categoryNames = agent.PreferredCategoryIds
                    .Where(id => categoryDict.ContainsKey(id))
                    .Select(id => categoryDict[id])
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
                PreferredCategoryIds = agent.PreferredCategoryIds ?? new List<int>(),
                PreferredCategoryNames = categoryNames,
                IncludeKeywords = agent.IncludeKeywords ?? new List<string>()
            };
        }
    }
}
