# Database Schema

This document describes the current SQL Server schema used by the scraper.

Source of truth:

- [sql/001_init.sql](sql/001_init.sql)

This schema is designed for Microsoft SQL Server and matches the current persistence code in [src/jobindex_scraper/persistence](src/jobindex_scraper/persistence).

## Overview

The schema is centered around these concepts:

- A scrape run records one execution of the scraper.
- A category represents one Jobindex category or subcategory.
- A job represents the canonical identity of a job posting across runs.
- A job observation records what was seen on a listing page during one run.
- A job snapshot records the extracted detail state for a job at one point in time.
- A job image stores downloaded banner and footer listing images.
- Join and event tables connect jobs to categories and record operational history.

## Table Summary

| Table | Purpose |
| --- | --- |
| `scrape_runs` | Lifecycle of one scraper execution |
| `categories` | Jobindex categories keyed by stable internal key |
| `jobs` | Canonical job identities |
| `job_observations` | Listing-page observations per run |
| `job_images` | Persisted banner and footer image binaries |
| `job_snapshots` | Extracted detail snapshots |
| `job_categories` | Many-to-many between jobs and categories |
| `job_keywords` | Future or downstream keyword enrichment |
| `scrape_events` | Operational and pipeline events |

## Tables

### `scrape_runs`

Tracks the lifecycle of one scraper run.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `scrape_run_id` | `UNIQUEIDENTIFIER` | No | Primary key |
| `started_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |
| `ended_at` | `DATETIME2(6)` | Yes | Filled when run completes or fails |
| `status` | `NVARCHAR(20)` | No | `running`, `completed`, `failed`, `cancelled` |
| `extraction_version` | `NVARCHAR(255)` | No | Version label for extractor behavior |
| `config_fingerprint` | `NVARCHAR(64)` | No | Stable hash of runtime config |
| `notes` | `NVARCHAR(MAX)` | Yes | JSON-ish summary or failure payload |

Constraints:

- Primary key on `scrape_run_id`
- Check constraint on `status`

### `categories`

Stores categories keyed by a stable internal key such as `subid_1`.

The scraper now refreshes `category_name` and `listing_url` from the actual Jobindex page metadata, so the row stores the real website category name and canonical category URL.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `category_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `category_key` | `NVARCHAR(255)` | No | Stable internal key, unique |
| `category_name` | `NVARCHAR(255)` | No | Real Jobindex category name |
| `listing_url` | `VARCHAR(900)` | No | Canonical Jobindex listing URL |
| `is_active` | `BIT` | No | Default `1` |
| `created_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `category_id`
- Unique constraint on `category_key`

### `jobs`

Stores canonical job identity and current state.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `canonical_job_url` | `VARCHAR(900)` | No | Canonical job URL, unique |
| `source_host` | `NVARCHAR(255)` | No | Host of the job detail URL |
| `current_listing_hash` | `CHAR(64)` | Yes | Latest listing hash seen |
| `first_seen_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |
| `last_seen_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |
| `last_detail_fetched_at` | `DATETIME2(6)` | Yes | Last time detail fetch ran |
| `last_http_status` | `SMALLINT` | Yes | Last detail fetch status |
| `current_snapshot_id` | `BIGINT` | Yes | FK to latest chosen snapshot |
| `is_active` | `BIT` | No | Default `1` |
| `created_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |
| `updated_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `job_id`
- Unique constraint on `canonical_job_url`
- Foreign key from `current_snapshot_id` to `job_snapshots.job_snapshot_id`

### `job_observations`

Stores the raw listing observation of a job on a listing page during one run.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_observation_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `scrape_run_id` | `UNIQUEIDENTIFIER` | No | FK to `scrape_runs` |
| `job_id` | `BIGINT` | No | FK to `jobs` |
| `category_id` | `BIGINT` | No | FK to `categories` |
| `listing_page_url` | `VARCHAR(900)` | No | Listing page where job was seen |
| `listing_position` | `INT` | No | Position on that listing page |
| `job_url_raw` | `VARCHAR(900)` | No | Raw href as seen in listing HTML |
| `job_title_raw` | `NVARCHAR(MAX)` | Yes | Raw title text |
| `company_name_raw` | `NVARCHAR(255)` | Yes | Raw company name |
| `company_url_raw` | `VARCHAR(900)` | Yes | Raw company URL |
| `location_raw` | `NVARCHAR(255)` | Yes | Raw location label |
| `published_raw` | `NVARCHAR(255)` | Yes | Raw publish label/date |
| `banner_image_url_raw` | `VARCHAR(900)` | Yes | Raw banner image URL from listing |
| `footer_image_url_raw` | `VARCHAR(900)` | Yes | Raw footer image URL from listing |
| `listing_hash` | `CHAR(64)` | No | Listing-derived change hash |
| `observed_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `job_observation_id`
- Foreign key to `scrape_runs`
- Foreign key to `jobs`
- Foreign key to `categories`
- Unique constraint on `(scrape_run_id, job_id, category_id, listing_page_url, listing_position)`

### `job_images`

Stores downloaded image binaries for banner and footer images.

This table is now actively used by the persistence layer.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_image_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `job_id` | `BIGINT` | No | FK to `jobs` |
| `image_role` | `NVARCHAR(20)` | No | `banner` or `footer` |
| `source_url` | `VARCHAR(900)` | No | Absolute source URL used to fetch image |
| `content_type` | `NVARCHAR(255)` | Yes | MIME type if known |
| `content_sha256` | `CHAR(64)` | No | Hash of image bytes |
| `image_bytes` | `VARBINARY(MAX)` | No | Binary image payload |
| `fetched_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `job_image_id`
- Foreign key to `jobs`
- Check constraint on `image_role` with values `banner`, `footer`
- Unique constraint on `(job_id, image_role, content_sha256)`

### `job_snapshots`

Stores extracted and normalized detail-page state for a job.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_snapshot_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `job_id` | `BIGINT` | No | FK to `jobs` |
| `extraction_version` | `NVARCHAR(255)` | No | Extractor version label |
| `listing_hash` | `CHAR(64)` | No | Listing hash associated with this snapshot |
| `detail_html_hash` | `CHAR(64)` | No | Hash of fetched detail HTML |
| `description_text_hash` | `CHAR(64)` | No | Hash of cleaned description text |
| `job_title_raw` | `NVARCHAR(MAX)` | Yes | Raw title captured for snapshot |
| `job_title_normalized` | `NVARCHAR(255)` | No | Normalized title |
| `company_name_raw` | `NVARCHAR(255)` | Yes | Raw company name |
| `company_name_normalized` | `NVARCHAR(255)` | Yes | Normalized company name |
| `company_url_raw` | `VARCHAR(900)` | Yes | Raw company URL |
| `company_url_normalized` | `VARCHAR(900)` | Yes | Normalized company URL |
| `location_raw` | `NVARCHAR(255)` | Yes | Raw location |
| `location_normalized` | `NVARCHAR(255)` | Yes | Normalized location |
| `published_raw` | `NVARCHAR(255)` | Yes | Raw published field |
| `published_utc` | `DATETIME2(6)` | Yes | Normalized publish timestamp |
| `job_description_raw` | `NVARCHAR(MAX)` | Yes | Raw description text |
| `job_description_clean` | `NVARCHAR(MAX)` | Yes | Cleaned description text |
| `field_provenance` | `NVARCHAR(MAX)` | No | JSON object |
| `extraction_warnings` | `NVARCHAR(MAX)` | No | JSON array |
| `dominant_language` | `NVARCHAR(50)` | Yes | Reserved for language enrichment |
| `language_confidence` | `FLOAT` | Yes | Reserved for language enrichment |
| `banner_image_id` | `BIGINT` | Yes | FK to `job_images` |
| `footer_image_id` | `BIGINT` | Yes | FK to `job_images` |
| `created_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `job_snapshot_id`
- Foreign key to `jobs`
- Foreign key to `job_images` for `banner_image_id`
- Foreign key to `job_images` for `footer_image_id`
- JSON check constraint on `field_provenance`
- JSON check constraint on `extraction_warnings`
- Unique constraint on `(job_id, extraction_version, detail_html_hash)`

### `job_categories`

Links jobs to categories.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_id` | `BIGINT` | No | FK to `jobs` |
| `category_id` | `BIGINT` | No | FK to `categories` |
| `linked_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Composite primary key on `(job_id, category_id)`
- Foreign key to `jobs`
- Foreign key to `categories`

### `job_keywords`

Reserved for keyword enrichment on top of snapshots.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `job_keyword_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `job_snapshot_id` | `BIGINT` | No | FK to `job_snapshots` |
| `keyword` | `NVARCHAR(255)` | No | Keyword text |
| `source` | `NVARCHAR(100)` | No | Source of keyword extraction |
| `confidence_score` | `FLOAT` | Yes | Optional score |
| `created_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `job_keyword_id`
- Foreign key to `job_snapshots`
- Unique constraint on `(job_snapshot_id, keyword, source)`

### `scrape_events`

Stores operational events emitted by the pipeline.

| Column | Type | Null | Notes |
| --- | --- | --- | --- |
| `scrape_event_id` | `BIGINT IDENTITY(1,1)` | No | Primary key |
| `scrape_run_id` | `UNIQUEIDENTIFIER` | No | FK to `scrape_runs` |
| `stage` | `NVARCHAR(100)` | No | Pipeline stage |
| `event` | `NVARCHAR(100)` | No | Event name |
| `status` | `NVARCHAR(20)` | No | `info`, `warning`, `error` |
| `canonical_job_url` | `VARCHAR(900)` | Yes | Optional job URL |
| `source_host` | `NVARCHAR(255)` | Yes | Optional source host |
| `details_json` | `NVARCHAR(MAX)` | No | JSON payload |
| `created_at` | `DATETIME2(6)` | No | Default `SYSUTCDATETIME()` |

Constraints:

- Primary key on `scrape_event_id`
- Foreign key to `scrape_runs`
- Check constraint on `status`
- JSON check constraint on `details_json`

## Relationships

- One `scrape_runs` row has many `job_observations` rows.
- One `scrape_runs` row has many `scrape_events` rows.
- One `categories` row has many `job_observations` rows.
- One `categories` row links to many `jobs` through `job_categories`.
- One `jobs` row has many `job_observations` rows.
- One `jobs` row has many `job_snapshots` rows.
- One `jobs` row has many `job_images` rows.
- One `jobs` row links to many `categories` through `job_categories`.
- One `jobs` row can point to one current snapshot through `current_snapshot_id`.
- One `job_snapshots` row can point to one banner image and one footer image.
- One `job_snapshots` row can have many `job_keywords` rows.

## Indexes

Current non-PK indexes:

- `idx_jobs_last_seen_at ON jobs (last_seen_at)`
- `idx_jobs_is_active ON jobs (is_active)`
- `idx_job_observations_job_id ON job_observations (job_id)`
- `idx_job_observations_scrape_run_id ON job_observations (scrape_run_id)`
- `idx_job_snapshots_job_id_created_at ON job_snapshots (job_id, created_at DESC)`
- `idx_job_categories_category_id_job_id ON job_categories (category_id, job_id)`
- `idx_job_keywords_snapshot_id ON job_keywords (job_snapshot_id)`
- `idx_scrape_events_run_stage ON scrape_events (scrape_run_id, stage)`

## SQL Server Notes

- URL fields that sit in unique or index-sensitive paths use `VARCHAR(900)` instead of `NVARCHAR(900)` to stay within SQL Server nonclustered key limits.
- JSON payloads are stored as `NVARCHAR(MAX)` with `ISJSON(...) = 1` constraints.
- Most tables use `BIGINT IDENTITY(1,1)` primary keys.
- Run identity uses `UNIQUEIDENTIFIER`.
- Timestamps default to `SYSUTCDATETIME()`.

## Runtime Notes

- Category rows are first created from a stable internal key such as `subid_1`, then updated during collection with the real Jobindex category name and canonical listing URL.
- Listing image URLs are stored in `job_observations` as raw URLs from Jobindex listing HTML.
- The detail persistence path now downloads those listing image URLs, writes the binaries into `job_images`, and links the resulting image IDs from `job_snapshots.banner_image_id` and `job_snapshots.footer_image_id`.
- Existing historic rows are not automatically backfilled; the image persistence path applies to new or refreshed snapshot writes.