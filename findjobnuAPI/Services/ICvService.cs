using FindjobnuService.DTOs;

namespace FindjobnuService.Services;

public interface ICvService
{
    Task<CvReadabilityResult> AnalyzeAsync(IFormFile pdfFile, CancellationToken cancellationToken = default);
    Task<CvImportResult> ImportToProfileAsync(string userId, IFormFile pdfFile, CancellationToken cancellationToken = default);
}
