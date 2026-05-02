USE [FindjobnuDB];
GO

-- Ensure full-text is enabled for the database (no-op if it already is)
IF (SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')) = 1
BEGIN
    IF DATABASEPROPERTYEX(DB_NAME(), 'IsFullTextEnabled') <> 1
    BEGIN
        EXEC sp_fulltext_database 'enable';
    END
END
ELSE
BEGIN
    THROW 50000, 'Full-Text Search feature is not installed on this SQL Server instance.', 1;
END
GO

-- Shared catalog for job posting entities
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'FTCatalog_JobIndex')
BEGIN
    CREATE FULLTEXT CATALOG FTCatalog_JobIndex WITH ACCENT_SENSITIVITY = OFF;
END
GO

-- Unique key for CONTAINSTABLE queries on job_snapshots
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_job_snapshots_FT' AND object_id = OBJECT_ID('dbo.job_snapshots'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_job_snapshots_FT ON dbo.job_snapshots(job_snapshot_id);
END
GO

-- Unique key for CONTAINSTABLE queries on job_keywords
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_job_keywords_FT' AND object_id = OBJECT_ID('dbo.job_keywords'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_job_keywords_FT ON dbo.job_keywords(job_keyword_id);
END
GO

-- Recreate the full-text index on job_snapshots normalized current-search fields
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.job_snapshots'))
BEGIN
    DROP FULLTEXT INDEX ON dbo.job_snapshots;
END
GO

CREATE FULLTEXT INDEX ON dbo.job_snapshots
(
    job_title_normalized LANGUAGE 1033,
    job_description_clean LANGUAGE 1033,
    company_name_normalized LANGUAGE 1033,
    location_normalized LANGUAGE 1033
)
KEY INDEX UX_job_snapshots_FT
ON FTCatalog_JobIndex
WITH CHANGE_TRACKING AUTO, STOPLIST = SYSTEM;
GO

-- Recreate the full-text index backing current-snapshot keyword lookups
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.job_keywords'))
BEGIN
    DROP FULLTEXT INDEX ON dbo.job_keywords;
END
GO

CREATE FULLTEXT INDEX ON dbo.job_keywords
(
    keyword LANGUAGE 1033
)
KEY INDEX UX_job_keywords_FT
ON FTCatalog_JobIndex
WITH CHANGE_TRACKING AUTO, STOPLIST = SYSTEM;
GO

-- Supporting nonclustered indexes used by filtering/pagination paths
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_snapshots_published_utc' AND object_id = OBJECT_ID('dbo.job_snapshots'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_snapshots_published_utc
        ON dbo.job_snapshots(published_utc DESC, job_id)
        INCLUDE (job_title_normalized, location_normalized, company_name_normalized);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_job_keywords_job_snapshot_id' AND object_id = OBJECT_ID('dbo.job_keywords'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_job_keywords_job_snapshot_id ON dbo.job_keywords(job_snapshot_id) INCLUDE (keyword);
END
GO

PRINT 'Full-text catalog and indexes for job_snapshots and job_keywords are now configured.';
