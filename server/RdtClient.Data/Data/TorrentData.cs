using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;

namespace RdtClient.Data.Data;

public class TorrentData(DataContext dataContext, ILogger<TorrentData>? logger = null) : ITorrentData
{
    public async Task<IList<Torrent>> Get()
    {
        var torrents = await dataContext.Torrents
                                         .AsNoTracking()
                                         .AsSplitQuery()
                                         .Include(m => m.Downloads)
                                         .OrderBy(m => m.Priority ?? 9999)
                                         .ToListAsync();

        return torrents.OrderBy(m => m.Priority ?? 9999)
                       .ThenBy(m => m.Added)
                       .ToList();
    }

    public async Task<Torrent?> GetById(Guid torrentId)
    {
        var dbTorrent = await dataContext.Torrents
                                         .AsNoTracking()
                                         .Include(m => m.Downloads)
                                         .FirstOrDefaultAsync(m => m.TorrentId == torrentId);

        if (dbTorrent == null)
        {
            return null;
        }

        foreach (var file in dbTorrent.Downloads)
        {
            file.Torrent = null;
        }

        return dbTorrent;
    }

    public async Task<Torrent?> GetByHash(String hash)
    {
        hash = hash.ToLower();

        var dbTorrent = await dataContext.Torrents
                                         .AsNoTracking()
                                         .Include(m => m.Downloads)
                                         .FirstOrDefaultAsync(m => m.Hash == hash);

        if (dbTorrent == null)
        {
            return null;
        }

        foreach (var file in dbTorrent.Downloads)
        {
            file.Torrent = null;
        }

        return dbTorrent;
    }

    public async Task<Torrent> Add(String? rdId,
                                   String hash,
                                   String? fileOrMagnetContents,
                                   Boolean isFile,
                                   DownloadType downloadType,
                                   DownloadClient downloadClient,
                                   Torrent torrent)
    {
        var newTorrent = new Torrent
        {
            TorrentId = Guid.NewGuid(),
            Added = DateTimeOffset.UtcNow,
            RdId = rdId,
            Hash = hash.ToLower(),
            Category = torrent.Category,
            HostDownloadAction = torrent.HostDownloadAction,
            FinishedActionDelay = torrent.FinishedActionDelay,
            DownloadAction = torrent.DownloadAction,
            FinishedAction = torrent.FinishedAction,
            DownloadMinSize = torrent.DownloadMinSize,
            IncludeRegex = torrent.IncludeRegex,
            ExcludeRegex = torrent.ExcludeRegex,
            DownloadManualFiles = torrent.DownloadManualFiles,
            DownloadClient = downloadClient,
            Type = downloadType,
            FileOrMagnet = fileOrMagnetContents,
            IsFile = isFile,
            Priority = torrent.Priority,
            TorrentRetryAttempts = torrent.TorrentRetryAttempts,
            DownloadRetryAttempts = torrent.DownloadRetryAttempts,
            DeleteOnError = torrent.DeleteOnError,
            Lifetime = torrent.Lifetime,
            RdStatus = torrent.RdStatus,
            RdName = torrent.RdName
        };

        await dataContext.Torrents.AddAsync(newTorrent);

        await dataContext.SaveChangesAsync();

        return newTorrent;
    }

    public async Task UpdateRdData(Torrent torrent)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrent.TorrentId)
                         .ExecuteUpdateAsync(s => s
                             .SetProperty(m => m.RdName, torrent.RdName)
                             .SetProperty(m => m.RdSize, torrent.RdSize)
                             .SetProperty(m => m.RdHost, torrent.RdHost)
                             .SetProperty(m => m.RdSplit, torrent.RdSplit)
                             .SetProperty(m => m.RdProgress, torrent.RdProgress)
                             .SetProperty(m => m.RdStatus, torrent.RdStatus)
                             .SetProperty(m => m.RdStatusRaw, torrent.RdStatusRaw)
                             .SetProperty(m => m.RdAdded, torrent.RdAdded)
                             .SetProperty(m => m.RdEnded, torrent.RdEnded)
                             .SetProperty(m => m.RdSpeed, torrent.RdSpeed)
                             .SetProperty(m => m.RdSeeders, torrent.RdSeeders)
                             .SetProperty(m => m.RdFiles, torrent.RdFiles));
    }

    public async Task UpdateRdId(Torrent torrent, String rdId)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrent.TorrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.RdId, rdId));
    }

    public async Task UpdateHash(Torrent torrent, String hash)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrent.TorrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.Hash, hash.ToLower()));
    }

    public async Task Update(Torrent torrent)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrent.TorrentId)
                         .ExecuteUpdateAsync(s => s
                             .SetProperty(m => m.DownloadClient, torrent.DownloadClient)
                             .SetProperty(m => m.HostDownloadAction, torrent.HostDownloadAction)
                             .SetProperty(m => m.Category, torrent.Category)
                             .SetProperty(m => m.Priority, torrent.Priority)
                             .SetProperty(m => m.DownloadRetryAttempts, torrent.DownloadRetryAttempts)
                             .SetProperty(m => m.TorrentRetryAttempts, torrent.TorrentRetryAttempts)
                             .SetProperty(m => m.DeleteOnError, torrent.DeleteOnError)
                             .SetProperty(m => m.Lifetime, torrent.Lifetime));
    }

    public async Task UpdateCategory(Guid torrentId, String? category)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.Category, category));
    }

    public async Task UpdateComplete(Guid torrentId, String? error, DateTimeOffset? datetime, Boolean retry)
    {
        var dbTorrent = await dataContext.Torrents.FirstOrDefaultAsync(m => m.TorrentId == torrentId);

        if (dbTorrent == null)
        {
            return;
        }

        if (String.IsNullOrWhiteSpace(error))
        {
            var downloads = await dataContext.Downloads.AsNoTracking().Where(m => m.TorrentId == torrentId).ToListAsync();
            var downloadWithErrors = downloads.Where(m => !String.IsNullOrWhiteSpace(m.Error)).ToList();

            if (downloadWithErrors.Count > 0)
            {
                error = $"{downloadWithErrors.Count}/{downloads.Count} downloads failed with errors";
            }
        }

        if (!String.IsNullOrWhiteSpace(error) && retry)
        {
            if (dbTorrent.RetryCount < dbTorrent.TorrentRetryAttempts)
            {
                dbTorrent.RetryCount += 1;
                dbTorrent.Retry = DateTime.UtcNow;
            }
        }

        dbTorrent.Completed = datetime;
        dbTorrent.Error = error;

        await dataContext.SaveChangesAsync();
    }

    public async Task UpdateFilesSelected(Guid torrentId, DateTimeOffset datetime)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.FilesSelected, datetime));
    }

    public async Task UpdatePriority(Guid torrentId, Int32? priority)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.Priority, priority));
    }

    public async Task UpdateRetry(Guid torrentId, DateTimeOffset? dateTime, Int32 retryCount)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteUpdateAsync(s => s
                             .SetProperty(m => m.RetryCount, retryCount)
                             .SetProperty(m => m.Retry, dateTime));
    }

    public async Task UpdateError(Guid torrentId, String error)
    {
        await dataContext.Torrents
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteUpdateAsync(s => s.SetProperty(m => m.Error, error));
    }

    public async Task Delete(Guid torrentId)
    {
        await using var transaction = await dataContext.Database.BeginTransactionAsync();

        await dataContext.Downloads
                         .Where(m => m.TorrentId == torrentId)
                         .ExecuteDeleteAsync();

        var deletedTorrents = await dataContext.Torrents
                                               .Where(m => m.TorrentId == torrentId)
                                               .ExecuteDeleteAsync();

        await transaction.CommitAsync();

        if (deletedTorrents == 0)
        {
            logger?.LogDebug("Skipped torrent graph deletion because the torrent was not found. TorrentId: {torrentId}", torrentId);

            return;
        }
    }
}
