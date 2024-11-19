﻿namespace ProxyMov_DownloadServer.Classes
{
    internal static class UserStorageHelper
    {
        private static UserModel? User = default;

        internal static void Set(UserModel user)
        {
            User = user;
        }

        internal static UserModel? Get()
        {
            return User;
        }
    }
}
