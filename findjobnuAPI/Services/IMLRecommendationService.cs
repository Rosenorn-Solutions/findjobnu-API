using FindjobnuService.Models;

namespace FindjobnuService.Services
{
    /// <summary>
    /// Machine Learning based job recommendation service
    /// Uses collaborative filtering and content-based filtering
    /// </summary>
    public interface IMLRecommendationService
    {
        /// <summary>
        /// Get ML-based job recommendations for a user
        /// </summary>
        /// <param name="userId">User identifier</param>
        /// <param name="profile">User profile</param>
        /// <param name="topN">Number of recommendations to return</param>
        /// <returns>List of recommended job IDs with scores</returns>
        Task<List<(int JobId, double Score)>> GetRecommendationsAsync(string userId, Profile profile, int topN = 100);

        /// <summary>
        /// Record a user interaction with a job for learning
        /// </summary>
        Task RecordInteractionAsync(string userId, int jobId, InteractionType interactionType, int? durationSeconds = null);

        /// <summary>
        /// Train or update the recommendation model (can be run periodically)
        /// </summary>
        Task UpdateModelAsync();
    }
}
