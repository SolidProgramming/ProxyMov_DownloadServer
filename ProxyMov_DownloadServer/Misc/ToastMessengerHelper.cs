using System.Net;
using Havit.Blazor.Components.Web;
using Havit.Blazor.Components.Web.Bootstrap;
using Microsoft.AspNetCore.Components;

namespace ProxyMov_DownloadServer.Misc;

public static class ToastMessengerHelper
{
    private const int DefaultAutoHideDelaySuccess = 3500;
    private const int DefaultAutoHideDelayInfo = 1500;
    private const int DefaultAutoHideDelayWarning = 4000;
    private const int DefaultAutoHideDelayError = 5000;
    private const int DefaultAutoHideDelaySecondary = 2500;

    public static void AddMessage(this IHxMessengerService messenger, string message, MessageType messageType)
    {
        int autoHideDelay;
        ThemeColor color;

        switch (messageType)
        {
            case MessageType.Success:
                autoHideDelay = DefaultAutoHideDelaySuccess;
                color = ThemeColor.Success;
                break;
            case MessageType.Info:
                autoHideDelay = DefaultAutoHideDelayInfo;
                color = ThemeColor.Info;
                break;
            case MessageType.Warning:
                autoHideDelay = DefaultAutoHideDelayWarning;
                color = ThemeColor.Warning;
                break;
            case MessageType.Error:
                autoHideDelay = DefaultAutoHideDelayError;
                color = ThemeColor.Danger;
                break;
            case MessageType.Secondary:
                autoHideDelay = DefaultAutoHideDelaySecondary;
                color = ThemeColor.Secondary;
                break;
            default:
                return;
        }

        BootstrapMessengerMessage toast = new()
        {
            Color = color,
            AutohideDelay = autoHideDelay,
            ContentTemplate = BuildContentTemplate("", message),
            CssClass = "mb-2"
        };

        messenger.AddMessage(toast);
    }

    private static RenderFragment BuildContentTemplate(string? title, string text)
    {
        return builder =>
        {
            if (title != null)
            {
                builder.OpenElement(1, "div");
                builder.AddAttribute(2, "class", "fw-bold");
                builder.AddContent(3, ProcessLineEndings(title));
                builder.CloseElement();
            }

            builder.AddContent(10, ProcessLineEndings(text));
        };
    }

    private static MarkupString ProcessLineEndings(string text)
    {
        return new MarkupString(WebUtility.HtmlEncode(text).ReplaceLineEndings("<br />"));
    }
}