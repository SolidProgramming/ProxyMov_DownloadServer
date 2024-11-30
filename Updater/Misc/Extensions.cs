namespace Updater.Misc
{
    internal static class Extensions
    {
        internal static async Task CopyToAsync(this Stream source, Stream destination, int bufferSize, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!source.CanRead)
                throw new ArgumentException("Has to be readable", nameof(source));

            ArgumentNullException.ThrowIfNull(destination);

            if (!destination.CanWrite)
                throw new ArgumentException("Has to be writable", nameof(destination));

            ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);

            var buffer = new byte[bufferSize];
            long totalBytesRead = 0;
            int bytesRead;
            while (( bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) ) != 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;
                progress?.Report(totalBytesRead);
            }
        }
        internal static async Task DownloadAsync(this HttpClient client, string requestUri, Stream destination, IProgress<float>? progress = null, CancellationToken cancellationToken = default)
        {
            // Get the http headers first to examine the content length
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            long? contentLength = response.Content.Headers.ContentLength;

            using Stream? download = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Ignore progress reporting when no progress reporter was 
            // passed or when the content length is unknown
            if (progress == null || !contentLength.HasValue)
            {
                await download.CopyToAsync(destination, cancellationToken);
                return;
            }

            Progress<long>? relativeProgress = new(totalBytes => progress.Report((float)totalBytes / contentLength.Value));

            // Use extension method to report progress while downloading
            await download.CopyToAsync(destination, 81920, relativeProgress, cancellationToken);
            progress.Report(1);
        }
    }
}
