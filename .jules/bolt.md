## 2024-05-18 - Optimized Database Sorting
**Learning:** Found an in-memory sort `torrents.OrderBy().ThenBy().ToList()` occurring right after a database query `dataContext.Torrents...OrderBy().ToListAsync()`. This is an inefficient pattern because Entity Framework can offload sorting to the database, saving CPU and memory overhead on the application server.
**Action:** When working with EF Core `.ToListAsync()` on large collections, always ensure `OrderBy` and `ThenBy` chains are applied *before* execution to perform database-side sorting.
