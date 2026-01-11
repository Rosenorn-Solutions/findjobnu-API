# Search Performance Optimization Scripts

This folder contains SQL scripts to optimize the job search and recommendations performance.

## Scripts Overview

| Script | Purpose | When to Run |
|--------|---------|-------------|
| `CREATE_FULLTEXT_Jobs.sql` | Full-text catalog and indexes for CONTAINSTABLE queries | Initial setup |
| `CREATE_SEARCH_INDEXES.sql` | Supporting indexes for location, date, and category filters | Initial setup |
| `CREATE_PROC_SearchJobs.sql` | Stored procedure for `/api/jobindexposts/search` | Initial setup |
| `CREATE_PROC_RecommendedJobs.sql` | Stored procedure for `/api/jobindexposts/recommended-jobs` | Initial setup |
| `DEPLOY_SearchOptimizations.sql` | Master script with deployment instructions | Initial setup |

## Deployment Order

Run the scripts in this order:

```sql
1. CREATE_FULLTEXT_Jobs.sql
2. CREATE_SEARCH_INDEXES.sql
3. CREATE_PROC_SearchJobs.sql
4. CREATE_PROC_RecommendedJobs.sql
```

Or use SQLCMD mode to run all at once:

```cmd
sqlcmd -S your-server -d FindjobnuDB -i DEPLOY_SearchOptimizations.sql
```

## Enabling Stored Procedures in Code

After deploying the stored procedures, enable them in `JobIndexPostsService`:

```csharp
// In Program.cs or service registration
builder.Services.AddScoped<IJobIndexPostsService>(sp =>
{
    var db = sp.GetRequiredService<FindjobnuContext>();
    var logger = sp.GetRequiredService<ILogger<JobIndexPostsService>>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    return new JobIndexPostsService(db, logger, cache) { UseStoredProcedures = true };
});
```

Or via configuration:

```csharp
// appsettings.json
{
  "SearchOptimization": {
    "UseStoredProcedures": true
  }
}

// In service
var useStoredProcs = configuration.GetValue<bool>("SearchOptimization:UseStoredProcedures");
service.UseStoredProcedures = useStoredProcs;
```

## Performance Improvements

| Optimization | Expected Impact |
|--------------|-----------------|
| Full-text indexes | Required for CONTAINSTABLE queries |
| `IX_JobIndexPostingsExtended_JobLocation` | Faster location filtering (LIKE queries) |
| `IX_JobIndexPostingsExtended_Published` | Faster date range queries |
| `IX_JobCategories_*` | Faster category EXISTS subqueries |
| Stored procedures | Query plan caching, reduced network traffic |
| Adaptive TOP_N_BY_RANK | 30-50% faster when filters present |

## Monitoring Performance

After deployment, monitor performance with these queries:

```sql
-- Check index usage
SELECT 
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    ius.user_seeks,
    ius.user_scans
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats ius 
    ON i.object_id = ius.object_id AND i.index_id = ius.index_id
WHERE OBJECT_NAME(i.object_id) = 'JobIndexPostingsExtended';

-- Check stored procedure stats
SELECT 
    OBJECT_NAME(object_id) AS ProcedureName,
    execution_count,
    total_elapsed_time / execution_count / 1000.0 AS avg_elapsed_ms
FROM sys.dm_exec_procedure_stats
WHERE OBJECT_NAME(object_id) LIKE 'usp_%Jobs';
```

## Troubleshooting

### Full-text population not complete
```sql
-- Check population status
SELECT * FROM sys.fulltext_indexes;

-- Force population if needed
ALTER FULLTEXT INDEX ON dbo.JobIndexPostingsExtended START FULL POPULATION;
```

### Slow queries despite indexes
1. Update statistics: `UPDATE STATISTICS dbo.JobIndexPostingsExtended WITH FULLSCAN;`
2. Check execution plan for missing indexes
3. Consider increasing `TOP_N_BY_RANK` limits if result sets are truncated

### Stored procedure not found
Ensure the stored procedures are created in the correct database:
```sql
USE FindjobnuDB;
SELECT * FROM sys.procedures WHERE name LIKE 'usp_%Jobs';
```
