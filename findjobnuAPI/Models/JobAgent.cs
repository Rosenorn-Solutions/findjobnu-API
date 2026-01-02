using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models
{
    public enum JobAgentFrequency
    {
        Daily = 1,
        Weekly = 2,
        Monthly = 3
    }

    public class JobAgent
    {
        [Key]
        public int Id { get; set; }

        // Foreign key to Profile
        [ForeignKey(nameof(Profile))]
        public int ProfileId { get; set; }
        public Profile Profile { get; set; } = null!;

        // Whether the agent is enabled (subscribed)
        public bool Enabled { get; set; } = true;

        // Frequency of emails, default Weekly if not provided
        public JobAgentFrequency Frequency { get; set; } = JobAgentFrequency.Weekly;

        // When the last email was sent
        public DateTime? LastSentAt { get; set; }

        // When to send the next email (computed by a scheduler/consumer)
        public DateTime? NextSendAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Token used for anonymous unsubscribe endpoint
        [StringLength(128)]
        public string? UnsubscribeToken { get; set; }

        // Optional filters applied after base recommendations are built
        public List<string>? PreferredLocations { get; set; } = new();
        public List<int>? PreferredCategoryIds { get; set; } = new();
        public List<string>? IncludeKeywords { get; set; } = new();
    }
}
