using FindjobnuService.DTOs;
using FindjobnuService.Models;

namespace FindjobnuService.Mappers
{
    public static class JobAgentMapper
    {
        public static JobAgentDto? ToDto(JobAgent? agent)
        {
            if (agent == null)
                return null;

            return new JobAgentDto
            {
                Id = agent.Id,
                ProfileId = agent.ProfileId,
                Enabled = agent.Enabled,
                Frequency = agent.Frequency.ToString(),
                LastSentAt = agent.LastSentAt,
                NextSendAt = agent.NextSendAt,
                CreatedAt = agent.CreatedAt,
                UpdatedAt = agent.UpdatedAt,
                PreferredLocations = agent.PreferredLocations ?? new List<string>(),
                PreferredCategoryIds = agent.PreferredCategoryIds ?? new List<int>(),
                IncludeKeywords = agent.IncludeKeywords ?? new List<string>()
            };
        }
    }
}
