namespace FindjobnuService.DTOs.Requests;

using FindjobnuService.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public record JobAgentUpdateRequest(
    [property: Required] bool Enabled,
    JobAgentFrequency? Frequency,
    IReadOnlyCollection<string>? PreferredLocations,
    IReadOnlyCollection<string>? PreferredCategoryKeys,
    IReadOnlyCollection<string>? IncludeKeywords
);
