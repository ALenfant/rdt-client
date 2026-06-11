## 2024-05-18 - [EF Core ExecuteUpdate Optimization]
**Learning:** In Entity Framework Core 7+, updating a single column by fetching the entity with `FirstOrDefaultAsync`, modifying it, and calling `SaveChangesAsync` creates a full roundtrip and unnecessary memory overhead. This pattern was prevalent in `DownloadData.cs` and `TorrentData.cs`.
**Action:** Replace these updates with `.ExecuteUpdateAsync(s => s.SetProperty(...))` to perform the update directly on the database side in a single query.
