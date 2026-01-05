# Machine Learning Job Recommendation System

## Overview
This implementation adds a sophisticated ML-based recommendation system to enhance job matching beyond simple keyword search. The system uses a hybrid approach combining multiple techniques for better recommendations.

## Architecture

### Components Created

1. **UserJobInteraction Model** (`Models/UserJobInteraction.cs`)
   - Tracks user behavior: views, clicks, saves, applications
   - Stores implicit feedback scores and interaction timestamps
   - Used for learning user preferences over time

2. **IMLRecommendationService Interface** (`Services/IMLRecommendationService.cs`)
   - Contract for ML recommendation operations
   - Supports recommendation generation, interaction tracking, and model updates

3. **MLRecommendationService** (`Services/MLRecommendationService.cs`)
   - Core ML recommendation engine
   - Implements hybrid recommendation algorithm

## Recommendation Algorithm

The system uses a **weighted scoring model** combining four approaches:

### 1. Content-Based Filtering (40% weight)
Matches jobs to user profile features:
- **Keyword matching**: Profile keywords vs job title/description/keywords
- **Skill proficiency weighting**: 
  - Expert skills: 4.0 points
  - Advanced skills: 3.0 points
  - Intermediate skills: 2.0 points
  - Beginner skills: 1.0 point
- **Category matching**: User interests vs job categories (3.0 points)
- **Location matching**: User location vs job location (2.0 points)

### 2. Collaborative Filtering (30% weight)
Finds similar users and recommends their jobs:
- Identifies users with overlapping job interactions
- Requires minimum 2 common jobs for similarity
- Weights recommendations by similarity strength
- Uses top 10 most similar users

### 3. Implicit Feedback (20% weight)
Learns from user's past behavior:
- **Interaction scores**:
  - View: 1 point
  - Click: 2 points
  - Save: 3 points
  - Apply: 5 points
  - Unsave: -3 points
- **Recency decay**: Exponential decay over 30 days
- **Duration bonus**: Long views (>60s) get +1.0 boost

### 4. Recency Score (10% weight)
Boosts newer job postings:
- Jobs ?7 days: 1.0
- Jobs 7-30 days: 1.0 ? 0.5 (linear decay)
- Jobs 30-90 days: 0.5 ? 0.0 (linear decay)
- Jobs >90 days: Not included

### Penalties
- **Applied jobs**: Multiplied by 0.1 (90% reduction)
- **Unsaved jobs**: Multiplied by 0.1 (90% reduction)

## Key Features

### Smart Caching
- Cache duration: **30 seconds** (configurable)
- Separate cache keys for ML vs keyword-based recommendations
- Automatic cache invalidation on new interactions

### Graceful Fallback
- If ML service unavailable ? Falls back to keyword-based recommendations
- Error handling prevents service disruptions
- Logged warnings for debugging

### Performance Optimizations
- Indexes on UserId, JobId, and interaction date
- Batch loading of jobs and interactions
- In-memory scoring after data retrieval
- Only considers jobs from last 3 months

## Integration

### JobIndexPostsService Updates
```csharp
// Constructor now accepts optional ML service
public JobIndexPostsService(
    FindjobnuContext db, 
    ILogger<JobIndexPostsService> logger, 
    IMemoryCache cache, 
    IMLRecommendationService? mlService = null)

// GetRecommendedJobsByUserAndProfile tries ML first, then falls back
if (_mlService != null)
{
    return BuildMLRecommendations(...);
}
return BuildRecommendations(...); // Original keyword-based
```

### Database Schema
```sql
CREATE TABLE UserJobInteractions (
    Id INT PRIMARY KEY IDENTITY,
    UserId NVARCHAR(450) NOT NULL,
    JobId INT NOT NULL,
    InteractionType INT NOT NULL,
    InteractionDate DATETIME2 NOT NULL,
    DurationSeconds INT NULL,
    Score INT NOT NULL,
    FOREIGN KEY (JobId) REFERENCES JobIndexPostingsExtended(JobID)
);

-- Indexes for performance
CREATE INDEX IX_UserJobInteractions_UserId ON UserJobInteractions(UserId);
CREATE INDEX IX_UserJobInteractions_JobId ON UserJobInteractions(JobId);
CREATE INDEX IX_UserJobInteractions_UserId_JobId ON UserJobInteractions(UserId, JobId);
CREATE INDEX IX_UserJobInteractions_InteractionDate ON UserJobInteractions(InteractionDate);
```

## Usage

### Recording User Interactions
```csharp
// Inject IMLRecommendationService in your endpoint/controller
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.View, durationSeconds: 45);
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Click);
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Save);
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Apply);
```

### Getting Recommendations
```csharp
// Existing endpoint automatically uses ML when available
GET /api/jobindexposts/recommended-jobs?page=1&pageSize=20

// Optional filters still work
GET /api/jobindexposts/recommended-jobs?location=Copenhagen&categoryId=5
```

## Migration Steps

### 1. Create Database Migration
```bash
cd findjobnuAPI
dotnet ef migrations add AddMLRecommendationSupport
dotnet ef database update
```

### 2. Add Interaction Tracking
Update your job viewing/clicking endpoints to record interactions:

```csharp
// In JobPostsEndpoints.cs or similar
group.MapGet("/{id}", async (
    int id, 
    HttpContext httpContext,
    IJobIndexPostsService jobService,
    IMLRecommendationService mlService) =>
{
    var job = await jobService.GetByIdAsync(id);
    
    // Record view interaction
    var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrEmpty(userId))
    {
        _ = mlService.RecordInteractionAsync(userId, id, InteractionType.View);
    }
    
    return TypedResults.Ok(job);
});
```

### 3. Track Save/Unsave Actions
```csharp
// When user saves a job
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Save);

// When user removes saved job
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Unsave);
```

### 4. Track Applications (if applicable)
```csharp
// When user applies to a job
await _mlService.RecordInteractionAsync(userId, jobId, InteractionType.Apply);
```

## Testing

Run the test suite:
```bash
cd FindJobNuTesting
dotnet test --filter "ClassName~MLRecommendationServiceTests"
```

Tests cover:
- Empty recommendations when no jobs exist
- Content-based scoring accuracy
- Implicit feedback integration
- Collaborative filtering
- Applied job penalties
- Caching behavior
- Interaction score assignments

## Performance Considerations

### Expected Query Performance
- **Recommendation generation**: ~100-500ms (depends on job count)
- **Interaction recording**: ~10-50ms
- **Cached recommendations**: <1ms

### Scaling Recommendations
For large datasets (>100K jobs), consider:
1. Pre-computing similarity matrices nightly
2. Using Redis for distributed caching
3. Implementing batch recommendation updates
4. Adding job vector embeddings for semantic search

### Memory Usage
- In-memory cache: ~1-5MB per user (30 second TTL)
- Database indexes: ~10-50MB per 100K interactions

## Future Enhancements

### Short-term
- [ ] Track click-through from recommendation list
- [ ] A/B testing framework for algorithm tuning
- [ ] User feedback loop (thumbs up/down)
- [ ] Diversity boost to prevent filter bubbles

### Long-term
- [ ] Deep learning embeddings (job2vec)
- [ ] Real-time model updates with online learning
- [ ] Multi-armed bandit for exploration vs exploitation
- [ ] Graph neural networks for company/skill networks
- [ ] Transformer-based semantic job matching

## Configuration

### appsettings.json
```json
{
  "MLRecommendations": {
    "CacheDurationSeconds": 30,
    "MaxRecommendations": 500,
    "MinSimilarUsers": 2,
    "MaxSimilarUsers": 10,
    "RecencyWindowDays": 90,
    "Weights": {
      "ContentBased": 0.4,
      "Collaborative": 0.3,
      "ImplicitFeedback": 0.2,
      "Recency": 0.1
    }
  }
}
```

## Monitoring

Key metrics to track:
- **Recommendation CTR**: Click-through rate on recommendations
- **Application rate**: % of recommendations leading to applications
- **Diversity score**: Variety of recommended jobs
- **Coverage**: % of jobs being recommended
- **Cold start**: Performance for new users

## Troubleshooting

### No recommendations showing
1. Check if user has profile data (keywords, skills, experience)
2. Verify jobs exist with `Published` date in last 3 months
3. Check logs for ML service errors

### Poor recommendation quality
1. Collect more interaction data (needs time)
2. Verify profile keywords match job content
3. Check if enough users exist for collaborative filtering
4. Review weight configuration

### Performance issues
1. Verify database indexes are created
2. Check cache hit rate in logs
3. Consider reducing `MaxRecommendations` value
4. Monitor database query execution time

## Credits

Implementation by: GitHub Copilot + Developer  
Algorithm: Hybrid Content-Based + Collaborative Filtering  
Inspiration: Netflix, Amazon, LinkedIn recommendation systems
