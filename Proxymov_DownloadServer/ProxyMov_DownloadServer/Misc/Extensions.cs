using System.Text.RegularExpressions;

namespace ProxyMov_DownloadServer.Misc;

internal static class Extensions
{
    private static readonly Dictionary<FileFormat, string> FileFormatsCollection = new()
    {
        { FileFormat.MKV, "matroska" },
        { FileFormat.FLV, "flv" },
        { FileFormat.ORIGINAL, "copy" },
        { FileFormat.MP4, "mp4" }
    };

    private static readonly Dictionary<FileFormat, string> FileFormatsNameCollection = new()
    {
        { FileFormat.MKV, "mkv" },
        { FileFormat.FLV, "flv" },
        { FileFormat.ORIGINAL, "original" },
        { FileFormat.MP4, "mp4" }
    };

    private static readonly Dictionary<VideoCodec, string> VideoCodecsCollection = new()
    {
        { VideoCodec.H264, "h264" },
        { VideoCodec.H264NVENC, "h264_nvenc" },
        { VideoCodec.H265, "hevc_nvenc" },
        { VideoCodec.MPEG4, "mpeg4" },
        { VideoCodec.VP8, "vp8" },
        { VideoCodec.VP9, "vp9" },
        { VideoCodec.ORIGINAL, "copy" }
    };

    private static readonly Dictionary<VideoCodec, string> VideoCodecsNameCollection = new()
    {
        { VideoCodec.H264, "h264" },
        { VideoCodec.H264NVENC, "h264 nvenc" },
        { VideoCodec.H265, "h265 nvenc" },
        { VideoCodec.MPEG4, "mpeg4" },
        { VideoCodec.VP8, "vp8" },
        { VideoCodec.VP9, "vp9" },
        { VideoCodec.ORIGINAL, "original" }
    };

    private static readonly Dictionary<AudioCodec, string> AudioCodecsCollection = new()
    {
        { AudioCodec.AAC, "aac" },
        { AudioCodec.AC3, "ac3" },
        { AudioCodec.MP3, "mp3" },
        { AudioCodec.ORIGINAL, "copy" }
    };

    private static readonly Dictionary<AudioCodec, string> AudioCodecsNameCollection = new()
    {
        { AudioCodec.AAC, "aac" },
        { AudioCodec.AC3, "ac3" },
        { AudioCodec.MP3, "mp3" },
        { AudioCodec.ORIGINAL, "original" }
    };

    private static readonly Dictionary<Language, string> VOELanguageKeyCollection = new()
    {
        { Language.GerDub, "1" },
        { Language.GerSub, "3" },
        { Language.EngDubGerSub, "3" },
        { Language.EngDub, "2" },
        { Language.EngSub, "2" }
    };

    internal static string ToVideoCodec(this VideoCodec vc)
    {
        return VideoCodecsCollection[vc];
    }

    internal static string ToVideoCodecName(this VideoCodec vc)
    {
        return VideoCodecsNameCollection[vc];
    }

    internal static VideoCodec ToVideoCodec(this string vc)
    {
        if (VideoCodecsCollection.ContainsValue(vc)) return VideoCodecsCollection.Single(x => x.Value == vc).Key;

        if (Enum.TryParse(vc, out VideoCodec codec) && VideoCodecsCollection.ContainsKey(codec)) return codec;

        return VideoCodec.ORIGINAL;
    }

    internal static string ToAudioCodec(this AudioCodec ac)
    {
        return AudioCodecsCollection[ac];
    }

    internal static string ToAudioCodecName(this AudioCodec ac)
    {
        return AudioCodecsNameCollection[ac];
    }

    internal static AudioCodec ToAudioCodec(this string ac)
    {
        return AudioCodecsCollection.Single(x => x.Value == ac).Key;
    }

    internal static string ToFileFormat(this FileFormat ff)
    {
        if (ff == FileFormat.MKV) return "mkv";

        return FileFormatsCollection[ff];
    }

    internal static string ToFileFormatName(this FileFormat ff)
    {
        return FileFormatsNameCollection[ff];
    }

    internal static FileFormat ToFileFormat(this string ff)
    {
        if (ff == "mkv") return FileFormat.MKV;

        return FileFormatsCollection.Single(x => x.Value == ff).Key;
    }

    internal static string GetValidFileName(this string name)
    {
        string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars()) +
                             new string(":");
        Regex r = new(string.Format("[{0}]", Regex.Escape(regexSearch)));
        return r.Replace(name, "");
    }

    internal static string UrlSanitize(this string text)
    {
        return text.Replace(' ', '-')
            .Replace(":", "")
            .Replace("~", "")
            .Replace("'", "")
            .Replace(",", "")
            .Replace("’", "");
    }

    internal static string? ToVOELanguageKey(this Language language)
    {
        if (VOELanguageKeyCollection.ContainsKey(language)) return VOELanguageKeyCollection[language];

        return null;
    }

    internal static string Repeat(this string text, int n)
    {
        ReadOnlySpan<char> textAsSpan = text.AsSpan();
        Span<char> span = new(new char[textAsSpan.Length * n]);
        for (int i = 0; i < n; i++) textAsSpan.CopyTo(span.Slice(i * textAsSpan.Length, textAsSpan.Length));

        return span.ToString();
    }

    public static Queue<T>? EnqueueRange<T>(this IEnumerable<T> source) where T : class
    {
        if (!source.Any()) return null;

        Queue<T> que = new();

        source.ToList().ForEach(que.Enqueue);

        return que;
    }

    public static IEnumerable<T> GetFlags<T>(this Enum input, Enum? ignore = null)
    {
        foreach (T value in Enum.GetValues(input.GetType()))
        {
            Enum enumVal = (Enum)Convert.ChangeType(value, typeof(Enum));
            if (!enumVal.Equals(ignore) && input.HasFlag(enumVal))
            {
                yield return value;
            }
        }
    }

    public static async Task<(bool success, string? ipv4)> GetIPv4(this HttpClient httpClient)
    {
        string result = await httpClient.GetStringAsync("https://api.ipify.org/");

        return (!string.IsNullOrEmpty(result), result);
    }
}