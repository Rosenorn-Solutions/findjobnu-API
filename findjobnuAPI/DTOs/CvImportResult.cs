namespace FindjobnuService.DTOs;

public record CvImportResult(
    ProfileDto Profile,
    CvReadabilitySummary Summary,
    string ExtractedText,
    bool CreatedProfile,
    IReadOnlyList<string> Warnings
);
