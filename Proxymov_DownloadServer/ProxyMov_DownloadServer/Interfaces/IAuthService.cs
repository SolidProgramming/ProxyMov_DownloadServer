namespace ProxyMov_DownloadServer.Interfaces;

internal interface IAuthService
{
    Task<bool> Login(string username, string password);
}