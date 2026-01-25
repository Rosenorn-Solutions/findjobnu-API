USE [FindjobnuDB];
GO

/*
 * Stored procedure for optimized job recommendations with profile keywords.
 * Similar to usp_SearchJobs but includes additional LIKE-based search term filtering.
 * 
 * Usage from C#:
 *   var results = await _db.JobIndexPosts
 *       .FromSqlRaw("EXEC dbo.usp_GetRecommendedJobs @ftQuery, @searchTerms, @locations, @categoryIds, @postedAfter, @postedBefore, @offset, @take", parameters)
 *       .ToListAsync();
 */

IF OBJECT_ID('dbo.usp_GetRecommendedJobs', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_GetRecommendedJobs;
GO

CREATE PROCEDURE dbo.usp_GetRecommendedJobs
    @ftQuery NVARCHAR(4000),                    -- Full-text query from profile keywords (e.g., '"developer" OR "C#"')
    @searchTerms NVARCHAR(1000) = NULL,         -- Comma-separated additional search terms for LIKE filtering
    @locations NVARCHAR(1000) = NULL,           -- Comma-separated location tokens
    @categoryIds NVARCHAR(500) = NULL,          -- Comma-separated category IDs
    @postedAfter DATETIME2 = NULL,              -- Filter: jobs posted on or after this date
    @postedBefore DATETIME2 = NULL,             -- Filter: jobs posted on or before this date
    @offset INT = 0,                            -- Pagination offset
    @take INT = 20,                             -- Pagination page size
    @mainTableTopN INT = 2000,                  -- TOP_N_BY_RANK limit for main table
    @keywordsTableTopN INT = 1000,              -- TOP_N_BY_RANK limit for keywords table
    @minRank INT = 30,                          -- Minimum full-text rank required for inclusion
    @totalCount INT OUTPUT                      -- Output: total matching records
AS
BEGIN
    SET NOCOUNT ON;
    SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

    -- Parse comma-separated locations (use different name than parameter)
    DECLARE @TblLocations TABLE (Token NVARCHAR(100));
    IF @locations IS NOT NULL AND LEN(@locations) > 0
    BEGIN
        INSERT INTO @TblLocations (Token)
        SELECT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@locations, ',')
        WHERE LEN(LTRIM(RTRIM(value))) > 0;
    END

    -- Parse comma-separated category IDs (use different name than parameter)
    DECLARE @TblCategories TABLE (CategoryID INT);
    IF @categoryIds IS NOT NULL AND LEN(@categoryIds) > 0
    BEGIN
        INSERT INTO @TblCategories (CategoryID)
        SELECT TRY_CAST(LTRIM(RTRIM(value)) AS INT)
        FROM STRING_SPLIT(@categoryIds, ',')
        WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) IS NOT NULL;
    END

    -- Parse comma-separated search terms for LIKE filtering (use different name than parameter)
    DECLARE @TblSearchTerms TABLE (Term NVARCHAR(200));
    IF @searchTerms IS NOT NULL AND LEN(@searchTerms) > 0
    BEGIN
        INSERT INTO @TblSearchTerms (Term)
        SELECT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@searchTerms, ',')
        WHERE LEN(LTRIM(RTRIM(value))) > 0;
    END

    -- CTE to get ranked job IDs from full-text search (profile keywords)
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
    -- Aggregate ranks and apply all filters
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
            -- Location filter
            AND (
                NOT EXISTS (SELECT 1 FROM @TblLocations)
                OR EXISTS (
                    SELECT 1 FROM @TblLocations lt 
                    WHERE j.JobLocation LIKE '%' + lt.Token + '%'
                )
            )
            -- Category filter
            AND (
                NOT EXISTS (SELECT 1 FROM @TblCategories)
                OR EXISTS (
                    SELECT 1 FROM dbo.JobCategories jc 
                    JOIN @TblCategories ci ON jc.CategoryID = ci.CategoryID
                    WHERE jc.JobID = j.JobID
                )
            )
            -- Additional search terms filter (LIKE-based, OR logic)
            AND (
                NOT EXISTS (SELECT 1 FROM @TblSearchTerms)
                OR EXISTS (
                    SELECT 1 FROM @TblSearchTerms st
                    WHERE j.JobTitle LIKE '%' + st.Term + '%'
                       OR j.CompanyName LIKE '%' + st.Term + '%'
                       OR j.JobDescription LIKE '%' + st.Term + '%'
                )
            )
        GROUP BY j.JobID
        HAVING MAX(ft.[RANK]) >= @minRank
    ),
    -- Get count and add row numbers
    CountedAndNumbered AS (
        SELECT 
            JobID,
            [RANK],
            COUNT(*) OVER() AS TotalCount,
            ROW_NUMBER() OVER(ORDER BY [RANK] DESC) AS RowNum
        FROM RankedJobs
    )
    -- Return paginated results with total count
    SELECT 
        j.*,
        cn.TotalCount
    FROM CountedAndNumbered cn
    JOIN dbo.JobIndexPostingsExtended j ON j.JobID = cn.JobID
    WHERE cn.RowNum > @offset AND cn.RowNum <= (@offset + @take)
    ORDER BY cn.[RANK] DESC, j.Published DESC;

    -- Set output parameter
    SELECT @totalCount = ISNULL(
        (SELECT TOP 1 TotalCount FROM (
            SELECT COUNT(*) OVER() AS TotalCount
            FROM (
                SELECT ft.JobID
                FROM (
                    SELECT t.[KEY] AS JobID, t.[RANK] AS [RANK]
                    FROM CONTAINSTABLE(dbo.JobIndexPostingsExtended, 
                        (JobTitle, JobDescription, CompanyName, JobLocation), 
                        @ftQuery, @mainTableTopN) t
                    UNION
                    SELECT j.JobID, tk.[RANK] AS [RANK]
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
                    AND (NOT EXISTS (SELECT 1 FROM @TblSearchTerms) OR EXISTS (SELECT 1 FROM @TblSearchTerms st WHERE j.JobTitle LIKE '%' + st.Term + '%' OR j.CompanyName LIKE '%' + st.Term + '%' OR j.JobDescription LIKE '%' + st.Term + '%'))
                GROUP BY ft.JobID
                HAVING MAX(ft.[RANK]) >= @minRank
            ) counted
        ) x), 0);
END
GO

PRINT 'Stored procedure usp_GetRecommendedJobs created successfully.';
GO
