USE [FindjobnuDB];
GO

/*
 * Performance indexes for JobIndexPostsService search operations on current snapshots.
 * These indexes optimize the WHERE clause filters used in SearchAsync and GetRecommendedJobsByUserAndProfile.
 * 
 * Run this script after the full-text indexes are created (CREATE_FULLTEXT_Jobs.sql).
 */

-- Note: location_normalized is searched with LIKE '%token%'.
-- The full-text index handles ranking, while this script focuses on join and posted-date support.
/*
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_snapshots_location_normalized' AND object_id = OBJECT_ID('dbo.job_snapshots'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_snapshots_location_normalized
        ON dbo.job_snapshots(location_normalized)
        INCLUDE (job_id, job_title_normalized, company_name_normalized, published_utc);
    PRINT 'Created IX_job_snapshots_location_normalized';
END
GO
*/

-- Covering index for job_categories join table (category filtering by category_key)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_categories_category_id_job_id' AND object_id = OBJECT_ID('dbo.job_categories'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_categories_category_id_job_id
        ON dbo.job_categories(category_id, job_id);
    PRINT 'Created IX_job_categories_category_id_job_id';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_categories_job_id_category_id' AND object_id = OBJECT_ID('dbo.job_categories'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_categories_job_id_category_id
        ON dbo.job_categories(job_id, category_id);
    PRINT 'Created IX_job_categories_job_id_category_id';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_snapshots_published_utc_job_id' AND object_id = OBJECT_ID('dbo.job_snapshots'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_snapshots_published_utc_job_id
        ON dbo.job_snapshots(published_utc DESC, job_id)
        INCLUDE (job_title_normalized, company_name_normalized, location_normalized);
    PRINT 'Created IX_job_snapshots_published_utc_job_id';
END
GO

-- Active current-state lookup for jobs used by all API read paths
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_jobs_is_active_current_snapshot_id' AND object_id = OBJECT_ID('dbo.jobs'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_jobs_is_active_current_snapshot_id
        ON dbo.jobs(is_active, current_snapshot_id)
        INCLUDE (canonical_job_url, source_host, last_seen_at);
    PRINT 'Created IX_jobs_is_active_current_snapshot_id';
END
GO

-- Category-key lookup for external filtering
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_categories_category_key' AND object_id = OBJECT_ID('dbo.categories'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_categories_category_key
        ON dbo.categories(category_key)
        INCLUDE (category_id, category_name, listing_url, is_active);
    PRINT 'Created IX_categories_category_key';
END
GO

-- Statistics update for query optimizer (run periodically)
UPDATE STATISTICS dbo.jobs WITH FULLSCAN;
UPDATE STATISTICS dbo.job_snapshots WITH FULLSCAN;
UPDATE STATISTICS dbo.job_categories WITH FULLSCAN;
UPDATE STATISTICS dbo.categories WITH FULLSCAN;
UPDATE STATISTICS dbo.job_keywords WITH FULLSCAN;
GO

PRINT 'Search performance indexes created successfully.';
