using System.Diagnostics;
using System.Net;

namespace ProxyMov_DownloadServer.Factories;

public static class ProxyFactory
{
    public static bool CreateProxy(ProxyAccountModel proxyAccount, out WebProxy? proxy)
    {
        proxy = null;

        if (string.IsNullOrEmpty(proxyAccount.Uri)) return false;

        try
        {
            proxy = new()
            {
                Address = new Uri(proxyAccount.Uri),
                BypassProxyOnLocal = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(proxyAccount.Username, proxyAccount.Password)
            };

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);

            return false;
        }
    }
}