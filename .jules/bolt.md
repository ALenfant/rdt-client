## 2023-10-27 - Entity Framework Core Database Property Update Tracking Overhead
**Learning:** For single or specific property updates/deletions, fetching the entire entity first causes unnecessary database queries and heavy Entity Framework Core tracking overhead.
**Action:** Use ExecuteUpdateAsync with SetProperty and ExecuteDeleteAsync when making non-relational property updates or row deletions directly to improve throughput and save memory.
