namespace FindjobnuService.DTOs
{
    using FindjobnuService.Models;

    public class JobAgentDto
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public bool Enabled { get; set; }
        public JobAgentFrequency Frequency { get; set; } = JobAgentFrequency.Weekly;
        public DateTime? LastSentAt { get; set; }
        public DateTime? NextSendAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<string> PreferredLocations { get; set; } = new();
        public List<int> PreferredCategoryIds { get; set; } = new();
        public List<string> PreferredCategoryNames { get; set; } = new();
        public List<string> IncludeKeywords { get; set; } = new();
    }
}
