namespace ProxyMov_DownloadServer.Enums
{
    [Flags]
    public enum Language
    {
        None = 0,
        GerDub = 1,
        EngSub = 2,
        GerSub = 4,
        EngDub = 8,
        EngDubGerSub = 16
    }
}
