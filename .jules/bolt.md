## 2025-01-20 - [Optimize Single-Property Updates using Entity Framework Core's ExecuteUpdateAsync]
**Learning:** Found a specific codebase pattern where many data layer methods fetched a full entity just to update a single property, adding tracking overhead and multiple database calls.
**Action:** Replaced single-property fetching/saving with `ExecuteUpdateAsync()` in Entity Framework Core, turning the whole operation into a single, efficient database round-trip without tracking overhead.
