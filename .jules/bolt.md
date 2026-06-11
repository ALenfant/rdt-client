## 2024-05-18 - ExecuteUpdateAsync for EF Core Performance
**Learning:** In Entity Framework Core, fetching an entire entity to update just a single property introduces unnecessary overhead via the Change Tracker and causes performance degradation during high-throughput updates (like rapidly changing download statuses or progress).
**Action:** Always use `ExecuteUpdateAsync` for atomic, single-property updates to bypass the Change Tracker, translating directly to an efficient `UPDATE` SQL statement.
