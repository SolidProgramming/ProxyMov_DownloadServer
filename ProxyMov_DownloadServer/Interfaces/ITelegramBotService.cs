using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ProxyMov_DownloadServer.Interfaces;

internal interface ITelegramBotService
{
    Task<Message?> SendMessageAsync(long chatId, string text, int? replyId = null, bool showLinkPreview = true,
        ParseMode parseMode = ParseMode.Html, bool silentMessage = false, ReplyKeyboardMarkup? rkm = null);

    Task SendChatAction(long chatId, ChatAction chatAction);

    Task<Message?> SendPhotoAsync(long chatId, string photoUrl, string? text = null,
        ParseMode parseMode = ParseMode.Html, bool silentMessage = false);
}