using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Models.Data;
using Download = RdtClient.Data.Models.Data.Download;

namespace RdtClient.Data.Data;

public class DownloadData(DataContext dataContext, ILogger<DownloadData>? logger = null)
{
    public async Task<List<Download>> GetForTorrent(Guid torrentId)
    {
        return await dataContext.Downloads
                                .AsNoTracking()
                                .Where(m => m.TorrentId == torrentId)
                                .ToListAsync();
    }

    public async Task<Download?> GetById(Guid downloadId)
    {
        return await dataContext.Downloads
                                .Include(m => m.Torrent)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(m => m.DownloadId == downloadId);
    }

    public async Task<Download?> Get(Guid torrentId, String path)
    {
        return await dataContext.Downloads
                                .Include(m => m.Torrent)
                                .AsNoTracking()
                                .FirstOrDefaultAsync(m => m.TorrentId == torrentId && m.Path == path);
    }

    public async Task<DownloadAddResult> TryAddForTorrent(Guid torrentId, DownloadInfo downloadInfo)
    {
        if (String.IsNullOrWhiteSpace(downloadInfo.RestrictedLink))
        {
            logger?.LogDebug("Skipped download creation because the restricted link was blank. TorrentId: {torrentId}", torrentId);

            return DownloadAddResult.InvalidInput;
        }

        if (!await dataContext.Torrents.AsNoTracking().AnyAsync(m => m.TorrentId == torrentId))
        {
            logger?.LogDebug("Skipped download creation because the torrent no longer exists. TorrentId: {torrentId}, Path: {path}", torrentId, downloadInfo.RestrictedLink);

            return DownloadAddResult.TorrentMissing;
        }

        if (await dataContext.Downloads.AsNoTracking().AnyAsync(m => m.TorrentId == torrentId && m.Path == downloadInfo.RestrictedLink))
        {
            logger?.LogDebug("Skipped download creation because it already exists. TorrentId: {torrentId}, Path: {path}", torrentId, downloadInfo.RestrictedLink);

            return DownloadAddResult.AlreadyExists;
        }

        var download = new Download
        {
            DownloadId = Guid.NewGuid(),
            TorrentId = torrentId,
            FileName = downloadInfo.FileName,
            Path = downloadInfo.RestrictedLink,
            Added = DateTimeOffset.UtcNow,
            DownloadQueued = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        await dataContext.Downloads.AddAsync(download);

        try
        {
            await dataContext.SaveChangesAsync();

            return DownloadAddResult.Added;
        }
        // These shouldn't be possible any longer, but added for safety and until confirmed.
        catch (DbUpdateException ex)
        {
            dataContext.Entry(download).State = EntityState.Detached;

            if (IsDuplicateDownloadViolation(ex))
            {
                logger?.LogDebug("Skipped download creation after a concurrent duplicate insert. TorrentId: {torrentId}, Path: {path}", torrentId, downloadInfo.RestrictedLink);

                return DownloadAddResult.AlreadyExists;
            }

            if (IsForeignKeyViolation(ex) && !await dataContext.Torrents.AsNoTracking().AnyAsync(m => m.TorrentId == torrentId))
            {
                logger?.LogDebug("Skipped download creation after the torrent was deleted concurrently. TorrentId: {torrentId}, Path: {path}", torrentId, downloadInfo.RestrictedLink);

                return DownloadAddResult.TorrentMissing;
            }

            throw;
        }
    }

    public async Task UpdateUnrestrictedLink(Guid downloadId, String unrestrictedLink)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.Link, unrestrictedLink));
    }

    public async Task UpdateFileName(Guid downloadId, String fileName)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.FileName, fileName));
    }

    public async Task UpdateDownloadStarted(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.DownloadStarted, dateTime));
    }

    public async Task UpdateDownloadFinished(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.DownloadFinished, dateTime));
    }

    public async Task UpdateUnpackingQueued(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.UnpackingQueued, dateTime));
    }

    public async Task UpdateUnpackingStarted(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.UnpackingStarted, dateTime));
    }

    public async Task UpdateUnpackingFinished(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.UnpackingFinished, dateTime));
    }

    public async Task UpdateCompleted(Guid downloadId, DateTimeOffset? dateTime)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.Completed, dateTime));
    }

    public async Task UpdateError(Guid downloadId, String? error)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.Error, error));
    }

    public async Task UpdateRetryCount(Guid downloadId, Int32 retryCount)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.RetryCount, retryCount));
    }

    public async Task UpdateRemoteId(Guid downloadId, String remoteId)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to avoid fetching the entity first
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(b => b.RemoteId, remoteId));
    }

    public async Task DeleteForTorrent(Guid torrentId)
    {
        // ⚡ Bolt: Using ExecuteDeleteAsync to avoid fetching entities first
        await dataContext.Downloads
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteDeleteAsync();
    }

    public async Task Reset(Guid downloadId)
    {
        // ⚡ Bolt: Using ExecuteUpdateAsync to update all required fields without fetching the entity
        var updated = await dataContext.Downloads
                                       .Where(m => m.DownloadId == downloadId)
                                       .ExecuteUpdateAsync(s => s.SetProperty(b => b.RetryCount, 0)
                                                                 .SetProperty(b => b.Link, (String?)null)
                                                                 .SetProperty(b => b.Added, DateTimeOffset.UtcNow)
                                                                 .SetProperty(b => b.DownloadQueued, DateTimeOffset.UtcNow)
                                                                 .SetProperty(b => b.DownloadStarted, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.DownloadFinished, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.UnpackingQueued, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.UnpackingStarted, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.UnpackingFinished, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.Completed, (DateTimeOffset?)null)
                                                                 .SetProperty(b => b.Error, (String?)null));

        if (updated == 0)
        {
            throw new($"Cannot find download with ID {downloadId}");
        }
    }

    private static Boolean IsDuplicateDownloadViolation(DbUpdateException exception)
    {
        var sqliteException = exception.InnerException as SqliteException;

        return sqliteException?.SqliteExtendedErrorCode == 2067
               || sqliteException?.Message.Contains("UNIQUE constraint failed: Downloads.TorrentId, Downloads.Path", StringComparison.Ordinal) == true;
    }

    private static Boolean IsForeignKeyViolation(DbUpdateException exception)
    {
        var sqliteException = exception.InnerException as SqliteException;

        return sqliteException?.SqliteExtendedErrorCode == 787
               || sqliteException?.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal) == true;
    }
}
