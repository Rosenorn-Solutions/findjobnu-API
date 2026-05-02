namespace FindjobnuService.DTOs.Responses
{
    public readonly record struct CategoryJobCountResponse(long Id, string CategoryKey, string CategoryName, string ListingUrl, bool IsActive, int NumberOfJobs);

    public struct CategoriesResponse(bool success, string? errorMessage, IReadOnlyList<CategoryJobCountResponse> categories)
    {
        public bool Success { get; set; } = success;
        public string? ErrorMessage { get; set; } = errorMessage;
        public IReadOnlyList<CategoryJobCountResponse> Categories { get; set; } = categories;
    }
}
