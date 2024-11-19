namespace ProxyMov_DownloadServer.Misc
{
    internal class WarningMessage
    {
        internal const string DownloadCanceled = "Download canceled!";
        internal const string ConverterDownloadStateNoData = "Converter invoked download but didn't pass download data to event!";
        internal const string StreamDurationTimeout = "Timeout getting Stream duration! Trying again on next download cycle.";
        internal const string FFMPEGExecutableFail = "Something went wrong during the FFMPEG execution!";
        internal const string DownloadNotRemoved = "Download is not removed from Database! Trying again on next download cycle.";
    }
}
