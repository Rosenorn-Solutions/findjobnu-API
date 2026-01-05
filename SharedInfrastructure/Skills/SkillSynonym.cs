namespace SharedInfrastructure.Skills;

/// <summary>
/// Represents a synonym or alias that maps to a canonical skill.
/// For example, "csharp", "c-sharp", "c sharp" all map to "C#".
/// </summary>
public class SkillSynonym
{
    public int Id { get; set; }

    /// <summary>
    /// The synonym/alias text (stored lowercase for case-insensitive matching).
    /// </summary>
    public string Synonym { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the canonical skill this synonym maps to.
    /// </summary>
    public int CanonicalSkillId { get; set; }

    /// <summary>
    /// Navigation property to the canonical skill.
    /// </summary>
    public CanonicalSkill? CanonicalSkill { get; set; }
}
