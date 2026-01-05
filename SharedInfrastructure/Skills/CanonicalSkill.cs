namespace SharedInfrastructure.Skills;

/// <summary>
/// Represents a canonical (normalized) skill in the taxonomy.
/// Similar to City, this provides a reference table for skill normalization.
/// </summary>
public class CanonicalSkill
{
    public int Id { get; set; }
    public Guid ExternalId { get; set; }

    /// <summary>
    /// The canonical display name of the skill (e.g., "C#", "JavaScript", "Project Management").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly slug for the skill (e.g., "csharp", "javascript", "project-management").
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Optional category for grouping skills (e.g., "Programming", "Soft Skills", "Healthcare").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Navigation property to synonyms that map to this canonical skill.
    /// </summary>
    public ICollection<SkillSynonym> Synonyms { get; set; } = new List<SkillSynonym>();
}
