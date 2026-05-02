using System.ComponentModel.DataAnnotations.Schema;

namespace FindjobnuService.Models;

[Table("job_categories")]
public class JobCategory
{
    [Column("job_id")]
    public long JobId { get; set; }

    [Column("category_id")]
    public long CategoryId { get; set; }

    [Column("linked_at")]
    public DateTime LinkedAt { get; set; }

    public Job Job { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
