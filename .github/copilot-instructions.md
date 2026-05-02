# Copilot Instructions

## Project Guidelines
- For findjobnuAPI job schema migration work: use jobs.current_snapshot_id as the source of the current job state; use normalized snapshot fields for search/filtering; use job_snapshots.published_utc as the posted date; switch category filtering/external identifiers to category_key; use only current-snapshot keywords; expose richer category metadata; update statistics for active jobs/current snapshot semantics; and replace inline image bytes with image endpoints.