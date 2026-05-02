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
 *       .FromSqlRaw("EXEC dbo.usp_SearchJobs @ftQuery, @locations, @categoryKeys, @postedAfter, @postedBefore, @offset, @take, @mainTableTopN, @keywordsTableTopN, @totalCount OUTPUT", parameters)
 *       .ToListAsync();
 */

IF OBJECT_ID('dbo.usp_SearchJobs', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_SearchJobs;
GO

CREATE PROCEDURE dbo.usp_SearchJobs
    @ftQuery NVARCHAR(4000),                    -- Full-text query string (e.g., '"developer" OR "engineer"')
    @locations NVARCHAR(1000) = NULL,           -- Comma-separated location tokens (e.g., 'København,Aarhus')
    @categoryKeys NVARCHAR(1000) = NULL,        -- Comma-separated category keys (e.g., 'it,healthcare')
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

    -- Parse comma-separated category keys into temp table
    DECLARE @TblCategories TABLE (CategoryKey NVARCHAR(255));
    IF @categoryKeys IS NOT NULL AND LEN(@categoryKeys) > 0
    BEGIN
        INSERT INTO @TblCategories (CategoryKey)
        SELECT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@categoryKeys, ',')
        WHERE LEN(LTRIM(RTRIM(value))) > 0;
    END

    -- CTE to get ranked active current-state job IDs from full-text search
    ;WITH FullTextMatches AS (
        SELECT s.job_id AS JobID, t.[RANK]
        FROM CONTAINSTABLE(dbo.job_snapshots,
            (job_title_normalized, job_description_clean, company_name_normalized, location_normalized),
            @ftQuery, @mainTableTopN) t
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = t.[KEY]
        UNION
        SELECT s.job_id AS JobID, tk.[RANK]
        FROM CONTAINSTABLE(dbo.job_keywords, keyword, @ftQuery, @keywordsTableTopN) tk
        JOIN dbo.job_keywords k ON k.job_keyword_id = tk.[KEY]
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = k.job_snapshot_id
    ),
    -- Aggregate ranks and apply filters against current snapshots only
    RankedJobs AS (
        SELECT 
            j.job_id AS JobID,
            MAX(ft.[RANK]) AS [RANK]
        FROM FullTextMatches ft
        JOIN dbo.jobs j ON j.job_id = ft.JobID
        JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
        WHERE 
            j.is_active = 1
            AND j.current_snapshot_id IS NOT NULL
            AND (@postedAfter IS NULL OR s.published_utc >= @postedAfter)
            AND (@postedBefore IS NULL OR s.published_utc <= @postedBefore)
            -- Location filter (OR logic across all tokens)
            AND (
                NOT EXISTS (SELECT 1 FROM @TblLocations)
                OR EXISTS (
                    SELECT 1 FROM @TblLocations lt 
                    WHERE s.location_normalized LIKE '%' + lt.Token + '%'
                )
            )
            -- Category filter (OR logic across all category keys)
            AND (
                NOT EXISTS (SELECT 1 FROM @TblCategories)
                OR EXISTS (
                    SELECT 1
                    FROM dbo.job_categories jc
                    JOIN dbo.categories c ON c.category_id = jc.category_id
                    JOIN @TblCategories ci ON c.category_key = ci.CategoryKey
                    WHERE jc.job_id = j.job_id
                )
            )
        GROUP BY j.job_id
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
        j.job_id AS JobID,
        s.company_name_normalized AS CompanyName,
        s.company_url_normalized AS CompanyURL,
        s.job_title_normalized AS JobTitle,
        s.job_description_clean AS JobDescription,
        s.location_normalized AS JobLocation,
        j.canonical_job_url AS JobUrl,
        s.published_utc AS Published,
        CASE WHEN s.banner_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/banner') END AS BannerImageUrl,
        CASE WHEN s.footer_image_id IS NULL THEN NULL ELSE CONCAT('/api/jobindexposts/', j.job_id, '/images/footer') END AS FooterImageUrl,
        j.source_host AS SourceHost,
        cn.TotalCount
    FROM CountedAndNumbered cn
    JOIN dbo.jobs j ON j.job_id = cn.JobID
    JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
    WHERE cn.RowNum > @offset AND cn.RowNum <= (@offset + @take)
    ORDER BY cn.[RANK] DESC, s.published_utc DESC;

    -- Set output parameter from the result (uses the TotalCount from window function)
    -- If no rows returned, count is 0
    SELECT @totalCount = ISNULL(
        (SELECT TOP 1 TotalCount FROM (
            SELECT COUNT(*) OVER() AS TotalCount
            FROM (
                SELECT ft.JobID
                FROM (
                    SELECT s.job_id AS JobID
                    FROM CONTAINSTABLE(dbo.job_snapshots,
                        (job_title_normalized, job_description_clean, company_name_normalized, location_normalized),
                        @ftQuery, @mainTableTopN) t
                    JOIN dbo.job_snapshots s ON s.job_snapshot_id = t.[KEY]
                    UNION
                    SELECT s.job_id
                    FROM CONTAINSTABLE(dbo.job_keywords, keyword, @ftQuery, @keywordsTableTopN) tk
                    JOIN dbo.job_keywords k ON k.job_keyword_id = tk.[KEY]
                    JOIN dbo.job_snapshots s ON s.job_snapshot_id = k.job_snapshot_id
                ) ft
                JOIN dbo.jobs j ON j.job_id = ft.JobID
                JOIN dbo.job_snapshots s ON s.job_snapshot_id = j.current_snapshot_id
                WHERE 
                    j.is_active = 1
                    AND j.current_snapshot_id IS NOT NULL
                    AND (@postedAfter IS NULL OR s.published_utc >= @postedAfter)
                    AND (@postedBefore IS NULL OR s.published_utc <= @postedBefore)
                    AND (NOT EXISTS (SELECT 1 FROM @TblLocations) OR EXISTS (SELECT 1 FROM @TblLocations lt WHERE s.location_normalized LIKE '%' + lt.Token + '%'))
                    AND (NOT EXISTS (SELECT 1 FROM @TblCategories) OR EXISTS (
                        SELECT 1
                        FROM dbo.job_categories jc
                        JOIN dbo.categories c ON c.category_id = jc.category_id
                        JOIN @TblCategories ci ON c.category_key = ci.CategoryKey
                        WHERE jc.job_id = j.job_id))
                GROUP BY ft.JobID
            ) counted
        ) x), 0);
END
GO

PRINT 'Stored procedure usp_SearchJobs created successfully.';
GO
