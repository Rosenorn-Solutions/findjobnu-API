# Recommended Jobs Endpoint

## Overview
`GET /api/jobindexposts/recommended-jobs` returns paginated job listings tailored to the authenticated user’s profile and optional query filters. The endpoint is part of `JobPostsEndpoints` and uses `IJobIndexPostsService.GetRecommendedJobsByUserAndProfile` for the recommendation logic.

## Authorization
- Requires authentication (group is decorated with `RequireAuthorization()`).
- Uses `ClaimTypes.NameIdentifier` from the access token to identify the user; missing/empty claim results in `401 Unauthorized`.

## Request (query parameters)
- `SearchTerms`: `string[]?` — additional free-text terms to bias recommendations.
- `Locations`: `string[]?` — location filters; only the first token of each value is used (e.g., `"København K"` ? `"København"`).
- `CategoryIds`: `int[]?` — job category filters (OR logic), only positive IDs are considered.
- `PostedAfter`: `DateTime?` — include jobs published on/after this date.
- `PostedBefore`: `DateTime?` — include jobs published on/before this date.
- `Page`: `int` — 1-based page index; defaults to `1`, coerced to `>= 1`.
- `PageSize`: `int` — defaults to `20`, coerced to `>= 1`; in the endpoint binding it is limited to `<= 200`.

## Behavior
1. **Guard clauses**
   - Rejects null request with `400 BadRequest`.
   - Rejects missing user identity with `401 Unauthorized`.
2. **Paging defaults** are applied inside the service (`page >= 1`, `pageSize >= 1`, default 20).
3. **Caching**
   - In-memory cache key is hashed from `userId`, filters, and paging.
   - Cached `PagedList<JobIndexPosts>` is reused for 2 minutes if present.
4. **Selectivity defaults**
   - Freshness: if no `PostedAfter` is supplied, recommendations default to the last 60 days.
   - Relevance: SQL recommendations apply a minimum full-text rank threshold (50) before paging (inline SQL path). Stored procedure path should implement the same threshold in the database routine.
5. **Profile-derived keywords**
   - Loads the caller’s profile (basic info, experiences, educations, interests, accomplishments, contacts, skills).
   - Builds a keyword set from: `Profile.Keywords`, `Interests.Title`, `Skills.Name`, `BasicInfo.JobTitle/Company`, and each experience’s `PositionTitle/Company`.
   - If the profile is missing or yields no keywords, returns an empty `PagedList` (the endpoint translates empty items to `204 NoContent`).
6. **Data provider branching**
   - **SQL Server**
     - Uses `JobSearchQueryBuilder` to construct a full-text query seeded with profile keywords.
     - Applies optional filters: date range, normalized locations (OR), categories (OR), and `SearchTerms` (LIKE search).
     - If `UseStoredProcedures` is `true`, executes `CREATE_PROC_RecommendedJobs.sql` (stored procedure) and hydrates categories separately; otherwise runs inline full-text SQL plus a dedicated count query.
   - **InMemory provider (tests/fallback)**
     - Filters in-memory jobs by profile keywords across title, company, description, location, categories, and associated keywords, then applies request filters (date, location, category, search terms), orders by `Published` descending, and paginates.
7. **Response shaping**
   - Service returns `PagedList<JobIndexPosts>` which the endpoint maps via `JobIndexPostsMapper.ToPagedDto` to `PagedResponse<JobIndexPostResponse>`.
   - Endpoint returns `200 OK` when `Items` is non-empty; otherwise `204 NoContent`.

## Response schema
- `PagedResponse<JobIndexPostResponse>`
  - `Items`: jobs on the current page.
  - `Page`: current page number.
  - `PageSize`: page size applied.
  - `TotalCount`: total matching jobs.
- `JobIndexPostResponse` fields: `Id`, `Title`, `Company`, `Location`, `JobUrl`, `PostedDate`, `Category`, `Description`, `CompanyUrl`, `BannerPicture`, `FooterPicture`, `BannerFormat`, `FooterFormat`, `BannerMimeType`, `FooterMimeType`.

## Status codes
- `200 OK`: recommendations found; returns `PagedResponse<JobIndexPostResponse>`.
- `204 NoContent`: no recommendations or profile keywords.
- `400 BadRequest`: null/invalid request model.
- `401 Unauthorized`: user identity missing.

## Operational notes
- In-memory cache duration: 2 minutes per unique `(userId, filters, page, pageSize)` combination.
- For best SQL performance, deploy and enable stored procedures (`CREATE_PROC_RecommendedJobs.sql`) and supporting full-text indexes referenced in `JobIndexPostsService` XML comments, then set `UseStoredProcedures = true` in configuration/DI setup for production.
