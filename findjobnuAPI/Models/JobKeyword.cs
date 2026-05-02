using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models
{
    [Table("job_keywords")]
    public class JobKeyword
    {
        [Key]
        [Column("job_keyword_id")]
        public long JobKeywordId { get; set; }

        [Column("job_snapshot_id")]
        public long JobSnapshotId { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("keyword")]
        public string Keyword { get; set; } = string.Empty;

        [MaxLength(100)]
        [Column("source")]
        public string? Source { get; set; }

        [Column("confidence_score")]
        public double? ConfidenceScore { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [NotMapped]
        public long KeywordID
        {
            get => JobKeywordId;
            set => JobKeywordId = value;
        }

        public JobSnapshot JobSnapshot { get; set; } = null!;
    }
}
