using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models;

[Table("job_snapshots")]
public class JobSnapshot
{
    [Key]
    [Column("job_snapshot_id")]
    public long JobSnapshotId { get; set; }

    [Column("job_id")]
    public long JobId { get; set; }

    [Column("extraction_version")]
    [MaxLength(255)]
    public string ExtractionVersion { get; set; } = string.Empty;

    [Column("listing_hash")]
    [MaxLength(64)]
    public string ListingHash { get; set; } = string.Empty;

    [Column("detail_html_hash")]
    [MaxLength(64)]
    public string DetailHtmlHash { get; set; } = string.Empty;

    [Column("description_text_hash")]
    [MaxLength(64)]
    public string DescriptionTextHash { get; set; } = string.Empty;

    [Column("job_title_raw")]
    public string? JobTitleRaw { get; set; }

    [Column("job_title_normalized")]
    [MaxLength(255)]
    public string JobTitleNormalized { get; set; } = string.Empty;

    [Column("company_name_raw")]
    [MaxLength(255)]
    public string? CompanyNameRaw { get; set; }

    [Column("company_name_normalized")]
    [MaxLength(255)]
    public string? CompanyNameNormalized { get; set; }

    [Column("company_url_raw")]
    [MaxLength(900)]
    public string? CompanyUrlRaw { get; set; }

    [Column("company_url_normalized")]
    [MaxLength(900)]
    public string? CompanyUrlNormalized { get; set; }

    [Column("location_raw")]
    [MaxLength(255)]
    public string? LocationRaw { get; set; }

    [Column("location_normalized")]
    [MaxLength(255)]
    public string? LocationNormalized { get; set; }

    [Column("published_raw")]
    [MaxLength(255)]
    public string? PublishedRaw { get; set; }

    [Column("published_utc")]
    public DateTime? PublishedUtc { get; set; }

    [Column("job_description_raw")]
    public string? JobDescriptionRaw { get; set; }

    [Column("job_description_clean")]
    public string? JobDescriptionClean { get; set; }

    [Column("field_provenance")]
    public string FieldProvenance { get; set; } = "{}";

    [Column("extraction_warnings")]
    public string ExtractionWarnings { get; set; } = "[]";

    [Column("dominant_language")]
    [MaxLength(50)]
    public string? DominantLanguage { get; set; }

    [Column("language_confidence")]
    public double? LanguageConfidence { get; set; }

    [Column("banner_image_id")]
    public long? BannerImageId { get; set; }

    [Column("footer_image_id")]
    public long? FooterImageId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public Job Job { get; set; } = null!;
    public JobImage? BannerImage { get; set; }
    public JobImage? FooterImage { get; set; }
    public ICollection<JobKeyword> Keywords { get; set; } = new List<JobKeyword>();
}
