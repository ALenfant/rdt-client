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
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.Link, unrestrictedLink));
    }

    public async Task UpdateFileName(Guid downloadId, String fileName)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.FileName, fileName));
    }

    public async Task UpdateDownloadStarted(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.DownloadStarted, dateTime));
    }

    public async Task UpdateDownloadFinished(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.DownloadFinished, dateTime));
    }

    public async Task UpdateUnpackingQueued(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.UnpackingQueued, dateTime));
    }

    public async Task UpdateUnpackingStarted(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.UnpackingStarted, dateTime));
    }

    public async Task UpdateUnpackingFinished(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.UnpackingFinished, dateTime));
    }

    public async Task UpdateCompleted(Guid downloadId, DateTimeOffset? dateTime)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.Completed, dateTime));
    }

    public async Task UpdateError(Guid downloadId, String? error)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.Error, error));
    }

    public async Task UpdateRetryCount(Guid downloadId, Int32 retryCount)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryCount, retryCount));
    }

    public async Task UpdateRemoteId(Guid downloadId, String remoteId)
    {
        await dataContext.Downloads
                         .Where(m => m.DownloadId == downloadId)
                         .ExecuteUpdateAsync(s => s.SetProperty(d => d.RemoteId, remoteId));
    }

    public async Task DeleteForTorrent(Guid torrentId)
    {
        await dataContext.Downloads
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteDeleteAsync();
    }

    public async Task Reset(Guid downloadId)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await dataContext.Downloads
                                       .Where(m => m.DownloadId == downloadId)
                                       .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryCount, 0)
                                                                 .SetProperty(d => d.Link, (String?)null)
                                                                 .SetProperty(d => d.Added, now)
                                                                 .SetProperty(d => d.DownloadQueued, now)
                                                                 .SetProperty(d => d.DownloadStarted, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.DownloadFinished, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.UnpackingQueued, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.UnpackingStarted, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.UnpackingFinished, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.Completed, (DateTimeOffset?)null)
                                                                 .SetProperty(d => d.Error, (String?)null));

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
