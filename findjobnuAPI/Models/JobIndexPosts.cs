using System.ComponentModel.DataAnnotations;

namespace FindjobnuService.Models
{
    public class JobIndexPosts
    {
        [Key]
        public long JobID { get; set; }
        public string? CompanyName { get; set; } = string.Empty;
        public string? CompanyURL { get; set; } = string.Empty;
        public string? JobTitle { get; set; } = string.Empty;
        public string? JobDescription { get; set; } = string.Empty;
        public string? JobLocation { get; set; } = string.Empty;
        public string? JobUrl { get; set; } = string.Empty;
        public DateTime? Published { get; set; }
        public ICollection<Category> Categories { get; set; } = new List<Category>();
        public string? BannerImageUrl { get; set; }
        public string? FooterImageUrl { get; set; }
        public string? SourceHost { get; set; }
    }
}
