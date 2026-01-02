namespace FindjobnuService.DTOs
{
    public class JobAgentDto
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public bool Enabled { get; set; }
        public string Frequency { get; set; } = "Weekly";
        public DateTime? LastSentAt { get; set; }
        public DateTime? NextSendAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<string> PreferredLocations { get; set; } = new();
        public List<int> PreferredCategoryIds { get; set; } = new();
        public List<string> IncludeKeywords { get; set; } = new();
    }
}
