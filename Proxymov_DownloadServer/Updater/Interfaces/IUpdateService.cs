using Updater.Models;

namespace Updater.Interfaces;

public interface IUpdateService
{
    event EventHandler OnUpdateCheckStarted;
    event EventHandler<(bool, UpdateDetailsModel?)> OnUpdateCheckFinished;

    Task CheckForUpdates(string assemblyVersion);
    Task DownloadUpdate(UpdateDetailsModel updateDetails, IProgress<float> progress);
}