﻿using System.Net;

namespace ProxyMov_DownloadServer.Factories
{
    public static class ProxyFactory
    {
        public static WebProxy? CreateProxy(ProxyAccountModel proxyAccount)
        {
            if (string.IsNullOrEmpty(proxyAccount.Uri))
                return null;

            return new WebProxy()
            {
                Address = new Uri(proxyAccount.Uri),
                BypassProxyOnLocal = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(proxyAccount.Username, proxyAccount.Password)
            };
        }
    }
}
