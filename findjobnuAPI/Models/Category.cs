using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace FindjobnuService.Models
{
    [Table("categories")]
    public class Category
    {
        [Key]
        [Column("category_id")]
        public long CategoryId { get; set; }

        [Column("category_key")]
        public string CategoryKey { get; set; } = string.Empty;

        [Column("category_name")]
        public string CategoryName { get; set; } = string.Empty;

        [Column("listing_url")]
        public string ListingUrl { get; set; } = string.Empty;

        [Column("is_active")]
        public bool IsActive { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [NotMapped]
        public long CategoryID
        {
            get => CategoryId;
            set => CategoryId = value;
        }

        [NotMapped]
        public string Name
        {
            get => CategoryName;
            set => CategoryName = value;
        }

        [JsonIgnore]
        public ICollection<JobCategory> JobCategories { get; set; } = new List<JobCategory>();
    }
}
