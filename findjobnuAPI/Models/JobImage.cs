using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models;

[Table("job_images")]
public class JobImage
{
    [Key]
    [Column("job_image_id")]
    public long JobImageId { get; set; }

    [Column("job_id")]
    public long JobId { get; set; }

    [Column("image_role")]
    [MaxLength(20)]
    public string ImageRole { get; set; } = string.Empty;

    [Column("source_url")]
    [MaxLength(900)]
    public string SourceUrl { get; set; } = string.Empty;

    [Column("content_type")]
    [MaxLength(255)]
    public string? ContentType { get; set; }

    [Column("content_sha256")]
    [MaxLength(64)]
    public string ContentSha256 { get; set; } = string.Empty;

    [Column("image_bytes")]
    public byte[] ImageBytes { get; set; } = [];

    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }

    public Job Job { get; set; } = null!;
}
