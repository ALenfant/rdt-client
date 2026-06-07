## 2026-06-07 - Optimize EF Core Database Updates
**Learning:** In Entity Framework Core, frequently changing entities using tracking queries (e.g. `FirstOrDefaultAsync`) followed by mutating properties and calling `SaveChangesAsync` introduces overhead due to the DB context having to track entities. For bulk updates or single property updates where we don't need the tracked entity afterwards, `ExecuteUpdateAsync` is much faster.
**Action:** Migrate basic property updates in EF Core repository classes (like `DownloadData.cs`) to use `ExecuteUpdateAsync` directly.
