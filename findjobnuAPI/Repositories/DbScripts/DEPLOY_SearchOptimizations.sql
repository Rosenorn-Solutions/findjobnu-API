USE [FindjobnuDB];
GO

/*
 * Master deployment script for search performance optimizations.
 * Run this script once to set up all indexes and stored procedures.
 * 
 * Prerequisites:
 * - Database FindjobnuDB must exist
 * - Tables jobs, job_snapshots, job_keywords, job_categories, categories must exist
 * - Full-Text Search feature must be installed on SQL Server
 * 
 * After running this script:
 * - Set UseStoredProcedures = true in JobIndexPostsService for best performance
 * 
 * Execution order:
 * 1. Full-text catalog and indexes (CREATE_FULLTEXT_Jobs.sql)
 * 2. Supporting indexes for filters (CREATE_SEARCH_INDEXES.sql)
 * 3. Search stored procedure (CREATE_PROC_SearchJobs.sql)
 * 4. Recommendations stored procedure (CREATE_PROC_RecommendedJobs.sql)
 */

PRINT '=== Starting Search Performance Optimization Deployment ===';
PRINT '';

-- Step 1: Full-text setup
PRINT 'Step 1/4: Setting up full-text catalog and indexes...';
-- (Include contents from CREATE_FULLTEXT_Jobs.sql or use :r if using SQLCMD mode)
GO

-- Step 2: Supporting indexes
PRINT 'Step 2/4: Creating supporting indexes for filters...';
-- (Include contents from CREATE_SEARCH_INDEXES.sql or use :r if using SQLCMD mode)
GO

-- Step 3: Search stored procedure
PRINT 'Step 3/4: Creating usp_SearchJobs stored procedure...';
-- (Include contents from CREATE_PROC_SearchJobs.sql or use :r if using SQLCMD mode)
GO

-- Step 4: Recommendations stored procedure
PRINT 'Step 4/4: Creating usp_GetRecommendedJobs stored procedure...';
-- (Include contents from CREATE_PROC_RecommendedJobs.sql or use :r if using SQLCMD mode)
GO

PRINT '';
PRINT '=== Deployment Complete ===';
PRINT '';
PRINT 'Next steps:';
PRINT '1. Verify indexes exist on jobs/job_snapshots/job_categories/categories/job_keywords';
PRINT '2. Verify stored procedures exist: SELECT * FROM sys.procedures WHERE name LIKE ''usp_%Jobs''';
PRINT '3. Enable stored procedures in code: Set UseStoredProcedures = true in JobIndexPostsService';
PRINT '4. Monitor performance with Query Store or Extended Events';
GO

/*
 * SQLCMD Mode Deployment (alternative):
 * If using SQLCMD mode in SSMS or sqlcmd.exe, uncomment and use these commands:
 *
 * :r "CREATE_FULLTEXT_Jobs.sql"
 * :r "CREATE_SEARCH_INDEXES.sql"
 * :r "CREATE_PROC_SearchJobs.sql"
 * :r "CREATE_PROC_RecommendedJobs.sql"
 */

/*
 * Performance Monitoring Queries:
 * 
 * -- Check index usage:
 * SELECT 
 *     OBJECT_NAME(i.object_id) AS TableName,
 *     i.name AS IndexName,
 *     ius.user_seeks,
 *     ius.user_scans,
 *     ius.user_lookups,
 *     ius.user_updates
 * FROM sys.indexes i
 * LEFT JOIN sys.dm_db_index_usage_stats ius 
 *     ON i.object_id = ius.object_id AND i.index_id = ius.index_id
 * WHERE OBJECT_NAME(i.object_id) IN ('jobs', 'job_snapshots', 'job_categories', 'categories', 'job_keywords')
 * ORDER BY TableName, IndexName;
 * 
 * -- Check stored procedure execution stats:
 * SELECT 
 *     OBJECT_NAME(object_id) AS ProcedureName,
 *     execution_count,
 *     total_elapsed_time / 1000.0 AS total_elapsed_ms,
 *     total_elapsed_time / execution_count / 1000.0 AS avg_elapsed_ms,
 *     last_execution_time
 * FROM sys.dm_exec_procedure_stats
 * WHERE OBJECT_NAME(object_id) IN ('usp_SearchJobs', 'usp_GetRecommendedJobs');
 * 
 * -- Check full-text index population status:
 * SELECT 
 *     OBJECT_NAME(object_id) AS TableName,
 *     change_tracking_state_desc,
 *     crawl_type_desc,
 *     crawl_start_date,
 *     crawl_end_date
 * FROM sys.fulltext_indexes;
 */
