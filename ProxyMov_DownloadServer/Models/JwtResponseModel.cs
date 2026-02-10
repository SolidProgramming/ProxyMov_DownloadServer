namespace ProxyMov_DownloadServer.Models;

public class JwtResponseModel(string token)
{
    public string Token { get; init; } = token;
}