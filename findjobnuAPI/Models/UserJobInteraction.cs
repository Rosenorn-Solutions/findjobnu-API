using System.ComponentModel.DataAnnotations;

namespace FindjobnuService.Models
{
    /// <summary>
    /// Tracks user interactions with job posts for ML-based recommendations
    /// </summary>
    public class UserJobInteraction
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public int JobId { get; set; }

        public InteractionType InteractionType { get; set; }

        public DateTime InteractionDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Time spent viewing the job in seconds (for view interactions)
        /// </summary>
        public int? DurationSeconds { get; set; }

        /// <summary>
        /// Implicit feedback score: higher = more interest
        /// View=1, Save=3, Click=2, Apply=5
        /// </summary>
        public int Score { get; set; }

        public JobIndexPosts? Job { get; set; }
    }

    public enum InteractionType
    {
        View = 1,
        Click = 2,
        Save = 3,
        Apply = 5,
        Unsave = -3
    }
}
