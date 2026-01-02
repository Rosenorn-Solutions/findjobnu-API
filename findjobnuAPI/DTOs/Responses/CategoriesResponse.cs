namespace FindjobnuService.DTOs.Responses
{
    public readonly record struct CategoryJobCountResponse(int Id, string Name, int NumberOfJobs);

    public struct CategoriesResponse(bool success, string? errorMessage, IReadOnlyList<CategoryJobCountResponse> categories)
    {
        public bool Success { get; set; } = success;
        public string? ErrorMessage { get; set; } = errorMessage;
        public IReadOnlyList<CategoryJobCountResponse> Categories { get; set; } = categories;
    }
}
