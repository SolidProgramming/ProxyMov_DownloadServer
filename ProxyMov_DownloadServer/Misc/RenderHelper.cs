namespace ProxyMov_DownloadServer.Misc
{
    internal static class RenderHelper
    {
        private static List<string> FirstRenderPages = [];

        internal static bool IsFirstRender(this string componentName) => !FirstRenderPages.Contains(componentName);

        internal static void ComponentSetRendered(string componentName)
        {
            if (!FirstRenderPages.Contains(componentName))
                FirstRenderPages.Add(componentName);
        }

        internal static void RemoveRenderEntry(string componentName) => FirstRenderPages.Remove(componentName);
    }
}
