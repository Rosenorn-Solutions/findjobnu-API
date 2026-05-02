using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models;

[Table("jobs")]
public class Job
{
    [Key]
    [Column("job_id")]
    public long JobId { get; set; }

    [Column("canonical_job_url")]
    [MaxLength(900)]
    public string CanonicalJobUrl { get; set; } = string.Empty;

    [Column("source_host")]
    [MaxLength(255)]
    public string SourceHost { get; set; } = string.Empty;

    [Column("current_listing_hash")]
    [MaxLength(64)]
    public string? CurrentListingHash { get; set; }

    [Column("first_seen_at")]
    public DateTime FirstSeenAt { get; set; }

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }

    [Column("last_detail_fetched_at")]
    public DateTime? LastDetailFetchedAt { get; set; }

    [Column("last_http_status")]
    public short? LastHttpStatus { get; set; }

    [Column("current_snapshot_id")]
    public long? CurrentSnapshotId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public JobSnapshot? CurrentSnapshot { get; set; }
    public ICollection<JobSnapshot> Snapshots { get; set; } = new List<JobSnapshot>();
    public ICollection<JobImage> Images { get; set; } = new List<JobImage>();
    public ICollection<JobCategory> JobCategories { get; set; } = new List<JobCategory>();
}
