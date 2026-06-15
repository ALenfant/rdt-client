using System.IO.Abstractions.TestingHelpers;
using RdtClient.Service.Services.Downloaders;
using Synology.Api.Client;
using Synology.Api.Client.Apis.DownloadStation.Info.Models;
using Synology.Api.Client.Apis.DownloadStation.Task.Models;
using Synology.Api.Client.Apis.FileStation.List.Models;
using Xunit.Abstractions;

namespace RdtClient.Service.Test.Services.Downloaders;

// Live end-to-end test against a real Virtual DSM. Skipped unless RDT_E2E_DSM_URL is set.
//
// Easiest: boot a throwaway DSM locally and run the whole chain (see scripts/e2e/README.md):
//   make e2e-vdsm-all
//
// Or against any DSM/vDSM you provide:
//   RDT_E2E_DSM_URL=http://127.0.0.1:55100 RDT_E2E_DSM_USER=admin RDT_E2E_DSM_PASS='...' \
//   RDT_E2E_ROOT=Downloads/rdt-e2e RDT_E2E_URL=http://speedtest.tele2.net/1MB.zip \
//   dotnet test --filter FullyQualifiedName~DownloadStationE2ETest
public class DownloadStationE2ETest(ITestOutputHelper output)
{
    private static String? Env(String key) => Environment.GetEnvironmentVariable(key);

    [Fact]
    public async Task E2E_CreatesFolder_CreatesTask_DownloadsFile_AndCleansUp()
    {
        var dsmUrl = Env("RDT_E2E_DSM_URL");

        if (String.IsNullOrWhiteSpace(dsmUrl))
        {
            output.WriteLine("RDT_E2E_DSM_URL not set; skipping live DSM E2E.");

            return;
        }

        var user = Env("RDT_E2E_DSM_USER")!;
        var pass = Env("RDT_E2E_DSM_PASS")!;
        var root = Env("RDT_E2E_ROOT") ?? "docker/rdt-e2e";
        var url = Env("RDT_E2E_URL") ?? "http://speedtest.tele2.net/1MB.zip";

        var torrentName = "rdt-e2e-" + Guid.NewGuid().ToString("N")[..8];
        var fileName = url.Split('/').Last();
        var relTail = $"{torrentName}/{fileName}";
        var remotePath = $"{root}/{relTail}";                  // share-relative, as Init builds it
        var filePath = $"/tmp/{relTail}";                       // local path (not used for the create-path assertions)

        output.WriteLine($"DSM={dsmUrl} root={root} torrent={torrentName} url={url}");

        var client = new SynologyClient(dsmUrl!);
        await client.LoginAsync(user, pass);
        output.WriteLine("Logged in.");

        // DownloadStation leaves every task in "Waiting" unless the account has a default destination.
        // Init() handles this in production (EnsureDefaultDestination); the test constructs the downloader
        // directly, so set it here against the configured root's shared folder.
        var dsConfig = await client.DownloadStationApi().InfoEndpoint().GetConfigAsync();
        if (String.IsNullOrWhiteSpace(dsConfig.DefaultDestination))
        {
            var shareRoot = root.Split('/')[0];
            var updated = new DownloadStationServerConfig(dsConfig.BtMaxDownloadSpeed,
                                                          dsConfig.BtMaxUploadSpeed,
                                                          dsConfig.EmulEnable,
                                                          dsConfig.EmulMaxDownloadSpeed,
                                                          dsConfig.EmulMaxUploadSpeed,
                                                          dsConfig.FtpMaxDownloadSpeed,
                                                          dsConfig.HttpMaxDownloadSpeed,
                                                          dsConfig.NzbMaxDownloadSpeed,
                                                          dsConfig.UnzipServiceEnable,
                                                          shareRoot,
                                                          dsConfig.EmulDefaultDestination);
            await client.DownloadStationApi().InfoEndpoint().SetServerConfigAsync(updated);
            output.WriteLine($"Set DownloadStation default destination to '{shareRoot}'.");
        }

        var downloader = new DownloadStationDownloader(null, url, remotePath, filePath, relTail, client);

        // --- The fix under test: Download() must create the destination folder, then the DS task. ---
        var gid = await downloader.Download();
        output.WriteLine($"Download() returned gid={gid}");
        Assert.False(String.IsNullOrWhiteSpace(gid), "Download() should return a Download Station task id");

        try
        {
            // Poll the real DS task until it finishes downloading or errors.
            DownloadStationTask? task = null;
            for (var i = 0; i < 90; i++)
            {
                task = await client.DownloadStationApi().TaskEndpoint().GetInfoAsync(gid);
                output.WriteLine($"  [{i:00}] status={task.Status} size={task.Size} dl={task.Additional?.Transfer?.SizeDownloaded} err={task.StatusExtra?.ErrorDetail}");

                Assert.True(String.IsNullOrWhiteSpace(task.StatusExtra?.ErrorDetail), $"DS reported error: {task.StatusExtra?.ErrorDetail}");

                if (task.Status is DownloadStationTaskStatus.Finished or DownloadStationTaskStatus.Downloaded or DownloadStationTaskStatus.Seeding)
                {
                    break;
                }

                await Task.Delay(2000);
            }

            Assert.NotNull(task);
            Assert.Contains(task!.Status, new[] { DownloadStationTaskStatus.Finished, DownloadStationTaskStatus.Downloaded, DownloadStationTaskStatus.Seeding });

            // Verify the file actually landed on the NAS at the expected per-torrent folder.
            var list = await client.FileStationApi().ListEndpoint().ListAsync(new FileStationListRequest($"/{root}/{torrentName}"));
            var names = list.Files?.Select(f => f.Name).ToList() ?? [];
            output.WriteLine($"Files in /{root}/{torrentName}: {String.Join(", ", names)}");
            Assert.Contains(fileName, names);

            // Bonus: Update() must read the real task status and signal completion once the file is present (file gate satisfied via MockFileSystem).
            var fs = new MockFileSystem();
            fs.AddFile(filePath, new MockFileData("x"));
            var downloader2 = new DownloadStationDownloader(gid, url, remotePath, filePath, relTail, client, new FakeDelayProvider(), fs);
            DownloadCompleteEventArgs? completed = null;
            downloader2.DownloadComplete += (_, e) => completed = e;
            await downloader2.Update();
            Assert.NotNull(completed);
            Assert.Null(completed!.Error);
            output.WriteLine("Update() signalled completion with no error.");
        }
        finally
        {
            await downloader.Cancel();
            output.WriteLine("Cancelled/removed the DS task.");
        }
    }
}
