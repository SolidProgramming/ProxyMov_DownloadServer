using System.Globalization;
using System.IO.Compression;
using System.Xml.Serialization;
using Updater.Interfaces;
using Updater.Misc;
using Updater.Models;

namespace Updater.Services;

public class UpdateService : IUpdateService
{
    private const string UpdatesDetailsUrl = "https://autoupdate.solidserver.xyz/proxymov_downloadserver/latest.xml";

    private const string UpdatesLatestUrl =
        "https://autoupdate.solidserver.xyz/proxymov_downloadserver/updates/latest.zip";

    private static readonly string DownloadsPath = Path.Combine(Directory.GetCurrentDirectory(), "Updates");
    private static readonly string AssemblyPath = Path.Combine(DownloadsPath, "latest.zip");
    private bool UpdateAvailable;

    private UpdateDetailsModel? UpdateDetails;
    public event EventHandler? OnUpdateCheckStarted;
    public event EventHandler<(bool, UpdateDetailsModel?)>? OnUpdateCheckFinished;

    public async Task CheckForUpdates(string assemblyVersion)
    {
        if (UpdateAvailable && UpdateDetails is not null) OnUpdateCheckFinished?.Invoke(this, (true, UpdateDetails));

        OnUpdateCheckStarted?.Invoke(this, EventArgs.Empty);

        await Task.Delay(2000);

        using HttpClient client = new();

        using CancellationTokenSource cts = new();
        cts.CancelAfter(2000);

        try
        {
            string result = await client.GetStringAsync(UpdatesDetailsUrl, cts.Token);

            if (result.Length > 0)
            {
                UpdateDetailsModel? updateDetails = ParseUpdateModel(result);

                if (updateDetails is null)
                {
                    OnUpdateCheckFinished?.Invoke(this, (false, null));
                    return;
                }

                if (updateDetails.Version != null && new Version(updateDetails.Version) > new Version(assemblyVersion))
                {
                    UpdateAvailable = true;
                    UpdateDetails = updateDetails;
                    OnUpdateCheckFinished?.Invoke(this, (UpdateAvailable, UpdateDetails));
                    return;
                }
            }

            OnUpdateCheckFinished?.Invoke(this, (false, null));
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public async Task DownloadUpdate(UpdateDetailsModel updateDetails, IProgress<float> progress)
    {
        using HttpClient? client = new();

        if (!Directory.Exists(DownloadsPath)) Directory.CreateDirectory(DownloadsPath);

        using FileStream? file = new(AssemblyPath, FileMode.Create, FileAccess.Write, FileShare.None);

        CancellationTokenSource cts = new();
        CancellationToken cancellationToken = cts.Token;

        await client.DownloadAsync(UpdatesLatestUrl, file, progress, cancellationToken);
    }

    public static async Task UnpackUpdate()
    {
        await Task.Run(() =>
        {
            try
            {
                ZipFile.ExtractToDirectory(AssemblyPath, DownloadsPath, true);
            }
            finally
            {
                if (File.Exists(AssemblyPath))
                {
                    File.Delete(AssemblyPath);
                }
            }
        });
    }

    private static UpdateDetailsModel? ParseUpdateModel(string xmlData)
    {
        try
        {
            XmlSerializer? serializer = new(typeof(UpdateDetailsModel));
            StringReader? rdr = new(xmlData);

            return Convert.ChangeType(serializer.Deserialize(rdr), typeof(UpdateDetailsModel),
                CultureInfo.InvariantCulture) as UpdateDetailsModel;
        }
        catch (Exception)
        {
            return null;
        }
    }
}