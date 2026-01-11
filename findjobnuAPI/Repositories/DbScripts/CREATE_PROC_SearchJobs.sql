USE [FindjobnuDB];
GO

/*
 * Stored procedure for optimized job search with full-text and filters.
 * Returns both paginated results and total count in a single execution.
 * 
 * Benefits over inline SQL:
 * - Query plan caching (significant for complex full-text queries)
 * - Reduced network traffic (no large SQL strings sent)
 * - Single result set with count (no separate count query needed)
 * - Easier to tune and analyze with Query Store
 * 
 * Usage from C#:
 *   var results = await _db.JobIndexPosts
 *       .FromSqlRaw("EXEC dbo.usp_SearchJobs @ftQuery, @locations, @categoryIds, @postedAfter, @postedBefore, @offset, @take, @totalCount OUTPUT", parameters)
 *       .ToListAsync();
 */

IF OBJECT_ID('dbo.usp_SearchJobs', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_SearchJobs;
GO

CREATE PROCEDURE dbo.usp_SearchJobs
    @ftQuery NVARCHAR(4000),                    -- Full-text query string (e.g., '"developer" OR "engineer"')
    @locations NVARCHAR(1000) = NULL,           -- Comma-separated location tokens (e.g., 'København,Aarhus')
    @categoryIds NVARCHAR(500) = NULL,          -- Comma-separated category IDs (e.g., '1,5,12')
    @postedAfter DATETIME2 = NULL,              -- Filter: jobs posted on or after this date
    @postedBefore DATETIME2 = NULL,             -- Filter: jobs posted on or before this date
    @offset INT = 0,                            -- Pagination offset
    @take INT = 20,                             -- Pagination page size
    @mainTableTopN INT = 2000,                  -- TOP_N_BY_RANK limit for main table (adaptive)
    @keywordsTableTopN INT = 1000,              -- TOP_N_BY_RANK limit for keywords table (adaptive)
    @totalCount INT OUTPUT                      -- Output: total matching records
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; -- Snapshot reads for better concurrency

    -- Parse comma-separated locations into temp table
    DECLARE @TblLocations TABLE (Token NVARCHAR(100));
    IF @locations IS NOT NULL AND LEN(@locations) > 0
    BEGIN
        INSERT INTO @TblLocations (Token)
        SELECT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@locations, ',')
        WHERE LEN(LTRIM(RTRIM(value))) > 0;
    END

    -- Parse comma-separated category IDs into temp table
    DECLARE @TblCategories TABLE (CategoryID INT);
    IF @categoryIds IS NOT NULL AND LEN(@categoryIds) > 0
    BEGIN
        INSERT INTO @TblCategories (CategoryID)
        SELECT TRY_CAST(LTRIM(RTRIM(value)) AS INT)
        FROM STRING_SPLIT(@categoryIds, ',')
        WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) IS NOT NULL;
    END

    -- CTE to get ranked job IDs from full-text search
    ;WITH FullTextMatches AS (
        SELECT t.[KEY] AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, 
            (JobTitle, JobDescription, CompanyName, JobLocation), 
            @ftQuery, @mainTableTopN) t
        UNION
        SELECT j.JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, @keywordsTableTopN) tk
        JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
    ),
    -- Aggregate ranks and apply filters
    RankedJobs AS (
        SELECT 
            j.JobID,
            MAX(ft.[RANK]) AS [RANK]
        FROM FullTextMatches ft
        JOIN dbo.JobIndexPostingsExtended j ON j.JobID = ft.JobID
        WHERE 
            -- Date range filters
            (@postedAfter IS NULL OR j.Published >= @postedAfter)
            AND (@postedBefore IS NULL OR j.Published <= @postedBefore)
            -- Location filter (OR logic across all tokens)
            AND (
                NOT EXISTS (SELECT 1 FROM @TblLocations)
                OR EXISTS (
                    SELECT 1 FROM @TblLocations lt 
                    WHERE j.JobLocation LIKE '%' + lt.Token + '%'
                )
            )
            -- Category filter (OR logic across all category IDs)
            AND (
                NOT EXISTS (SELECT 1 FROM @TblCategories)
                OR EXISTS (
                    SELECT 1 FROM dbo.JobCategories jc 
                    JOIN @TblCategories ci ON jc.CategoryID = ci.CategoryID
                    WHERE jc.JobID = j.JobID
                )
            )
        GROUP BY j.JobID
    ),
    -- Get count and add row numbers for pagination
    CountedAndNumbered AS (
        SELECT 
            JobID,
            [RANK],
            COUNT(*) OVER() AS TotalCount,
            ROW_NUMBER() OVER(ORDER BY [RANK] DESC) AS RowNum
        FROM RankedJobs
    )
    -- Return paginated results
    SELECT 
        j.*,
        cn.TotalCount
    FROM CountedAndNumbered cn
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = cn.JobID
    WHERE cn.RowNum > @offset AND cn.RowNum <= (@offset + @take)
    ORDER BY cn.[RANK] DESC, j.Published DESC;

    -- Set output parameter from the result (uses the TotalCount from window function)
    -- If no rows returned, count is 0
    SELECT @totalCount = ISNULL(
        (SELECT TOP 1 TotalCount FROM (
            SELECT COUNT(*) OVER() AS TotalCount
            FROM (
                SELECT ft.JobID
                FROM (
                    SELECT t.[KEY] AS JobID
                    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, 
                        (JobTitle, JobDescription, CompanyName, JobLocation), 
                        @ftQuery, @mainTableTopN) t
                    UNION
                    SELECT j.JobID
                    FROM CONTAINSTABLE(dbo.JobKeywords, Keyword, @ftQuery, @keywordsTableTopN) tk
                    JOIN dbo.JobKeywords k ON k.KeywordID = tk.[KEY]
                    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = k.JobID
                ) ft
                JOIN dbo.JobIndexPostingsExtended j ON j.JobID = ft.JobID
                WHERE 
                    (@postedAfter IS NULL OR j.Published >= @postedAfter)
                    AND (@postedBefore IS NULL OR j.Published <= @postedBefore)
                    AND (NOT EXISTS (SELECT 1 FROM @TblLocations) OR EXISTS (SELECT 1 FROM @TblLocations lt WHERE j.JobLocation LIKE '%' + lt.Token + '%'))
                    AND (NOT EXISTS (SELECT 1 FROM @TblCategories) OR EXISTS (SELECT 1 FROM dbo.JobCategories jc JOIN @TblCategories ci ON jc.CategoryID = ci.CategoryID WHERE jc.JobID = j.JobID))
                GROUP BY ft.JobID
            ) counted
        ) x), 0);
END
GO

PRINT 'Stored procedure usp_SearchJobs created successfully.';
GO
