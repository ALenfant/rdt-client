## 2026-05-27 - Entity Framework Core Batch Updates
**Learning:** Refactored repetitive `FirstOrDefaultAsync` + `SaveChanges` into direct `ExecuteUpdateAsync` updates across `DownloadData`, `TorrentData` and `SettingData`. The original pattern fetched the entity into memory to change a single field (e.g. `UpdateFileName`, `UpdateDownloadStarted`).
**Action:** Always scan for single-property updates that fetch the entire entity, and replace them with `ExecuteUpdateAsync` to save a DB round trip and tracking overhead.
