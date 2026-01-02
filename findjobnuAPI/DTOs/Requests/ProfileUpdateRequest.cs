namespace FindjobnuService.DTOs.Requests;

using System.ComponentModel.DataAnnotations;

public record ProfileUpdateRequest(
    [property: Required] string UserId,
    // Basic info
    [property: Required, MaxLength(50)] string FirstName,
    [property: Required, MaxLength(100)] string LastName,
    DateTime? DateOfBirth,
    [property: Phone, MaxLength(100)] string? PhoneNumber,
    [property: MaxLength(2000)] string? About,
    [property: MaxLength(200)] string? Location,
    [property: MaxLength(200)] string? Company,
    [property: MaxLength(200)] string? JobTitle,
    [property: MaxLength(500), Url] string? LinkedinUrl,
    bool OpenToWork,
    // Collections
    List<ExperienceUpdate>? Experiences,
    List<EducationUpdate>? Educations,
    List<InterestUpdate>? Interests,
    List<AccomplishmentUpdate>? Accomplishments,
    List<ContactUpdate>? Contacts,
    List<SkillUpdate>? Skills,
    // Other profile fields
    List<string>? Keywords,
    List<string>? SavedJobPosts
);

public record ExperienceUpdate(
    [property: MaxLength(200)] string? PositionTitle,
    [property: MaxLength(200)] string? Company,
    string? FromDate,
    string? ToDate,
    string? Duration,
    [property: MaxLength(200)] string? Location,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? LinkedinUrl
);

public record EducationUpdate(
    [property: MaxLength(200)] string? Institution,
    [property: MaxLength(200)] string? Degree,
    string? FromDate,
    string? ToDate,
    [property: MaxLength(2000)] string? Description,
    [property: MaxLength(500), Url] string? LinkedinUrl
);

public record InterestUpdate(
    [property: MaxLength(200)] string? Title
);

public record AccomplishmentUpdate(
    [property: MaxLength(100)] string? Category,
    [property: MaxLength(200)] string? Title
);

public record ContactUpdate(
    [property: MaxLength(200)] string? Name,
    [property: MaxLength(200)] string? Occupation,
    [property: MaxLength(500), Url] string? Url
);

public enum SkillProficiencyUpdate
{
    Beginner,
    Intermediate,
    Advanced,
    Expert
}

public record SkillUpdate(
    [property: Required, MaxLength(200)] string Name,
    [property: Required] SkillProficiencyUpdate Proficiency
);
