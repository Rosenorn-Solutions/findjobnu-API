USE [FindjobnuDB];
GO

/*
 * Performance indexes for JobIndexPostsService search operations.
 * These indexes optimize the WHERE clause filters used in SearchAsync and GetRecommendedJobsByUserAndProfile.
 * 
 * Run this script after the full-text indexes are created (CREATE_FULLTEXT_Jobs.sql).
 */

-- Note: JobLocation is likely NVARCHAR(MAX) and cannot be directly indexed.
-- The full-text index on JobLocation (in CREATE_FULLTEXT_Jobs.sql) handles text search.
-- For LIKE '%city%' queries, SQL Server will scan but the full-text index helps for exact matches.
-- If JobLocation is changed to NVARCHAR(200) in the future, uncomment this index:
/*
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobIndexPostingsExtended_JobLocation' AND object_id = OBJECT_ID('dbo.JobIndexPostingsExtended'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobIndexPostingsExtended_JobLocation
        ON dbo.JobIndexPostingsExtended(JobLocation)
        INCLUDE (JobID, JobTitle, CompanyName, Published);
    PRINT 'Created IX_JobIndexPostingsExtended_JobLocation';
END
GO
*/

-- Covering index for JobCategories join table (category filtering)
-- Optimizes the EXISTS subquery: EXISTS (SELECT 1 FROM JobCategories jc WHERE jc.JobID = j.JobID AND jc.CategoryID IN (...))
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobCategories_CategoryID_JobID' AND object_id = OBJECT_ID('dbo.JobCategories'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobCategories_CategoryID_JobID
        ON dbo.JobCategories(CategoryID, JobID);
    PRINT 'Created IX_JobCategories_CategoryID_JobID';
END
GO

-- Reverse index for JobCategories (when filtering by JobID first)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobCategories_JobID_CategoryID' AND object_id = OBJECT_ID('dbo.JobCategories'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobCategories_JobID_CategoryID
        ON dbo.JobCategories(JobID, CategoryID);
    PRINT 'Created IX_JobCategories_JobID_CategoryID';
END
GO

-- Composite index for date range + sorting (common query pattern)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobIndexPostingsExtended_Published_JobID' AND object_id = OBJECT_ID('dbo.JobIndexPostingsExtended'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_JobIndexPostingsExtended_Published_JobID
        ON dbo.JobIndexPostingsExtended(Published DESC, JobID)
        INCLUDE (JobTitle, CompanyName, JobUrl);
    PRINT 'Created IX_JobIndexPostingsExtended_Published_JobID';
END
GO

-- Statistics update for query optimizer (run periodically)
UPDATE STATISTICS dbo.JobIndexPostingsExtended WITH FULLSCAN;
UPDATE STATISTICS dbo.JobCategories WITH FULLSCAN;
UPDATE STATISTICS dbo.JobKeywords WITH FULLSCAN;
GO

PRINT 'Search performance indexes created successfully.';
