using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FindjobnuService.Models
{
    [Index(nameof(Email), IsUnique = true)]
    public class NewsletterSubscription
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(320)]
        public string Email { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
