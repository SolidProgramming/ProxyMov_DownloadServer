namespace ProxyMov_DownloadServer.Models;

public class ConvertProgressModel
{
    public long Size { get; set; }
    public TimeSpan Time { get; set; }
    public double Bitrate { get; set; }
    public double EncodingSpeed { get; set; }
    public double FPS { get; set; }
    public int ProgressPercent { get; set; }
    public TimeSpan ETA { get; set; }
    public double KBytePerSecond { get; set; }
}