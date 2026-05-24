## 2024-05-24 - Entity Framework Core ExecuteUpdateAsync
**Learning:** Found a common anti-pattern in the codebase where single-property updates were performed by fetching the entire entity (via `FirstOrDefaultAsync`), updating the property, and then calling `SaveChangesAsync`. This causes an unnecessary database round-trip (SELECT) and creates memory/tracking overhead, which is detrimental for a torrent client that does frequent status updates.
**Action:** Use EF Core's `ExecuteUpdateAsync` for direct, single-query updates without fetching the entity.
