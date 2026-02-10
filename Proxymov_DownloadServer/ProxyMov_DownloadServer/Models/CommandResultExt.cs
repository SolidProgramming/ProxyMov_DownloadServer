using CliWrap;

namespace ProxyMov_DownloadServer.Models;

public class CommandResultExt(
    int exitCode = 0,
    DateTimeOffset startTime = default,
    DateTimeOffset exitTime = default,
    bool skippedNoResult = false,
    bool skipped = false) : CommandResult(exitCode, startTime, exitTime)
{
    public bool SkippedNoResult { get; internal set; } = skippedNoResult;
    public bool Skipped { get; internal set; } = skipped;
}