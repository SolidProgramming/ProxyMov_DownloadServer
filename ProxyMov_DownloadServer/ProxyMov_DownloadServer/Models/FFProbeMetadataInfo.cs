using Newtonsoft.Json;

namespace ProxyMov_DownloadServer.Models
{
    public class Disposition
    {
        [JsonProperty("default")]
        public int? Default;

        [JsonProperty("dub")]
        public int? Dub;

        [JsonProperty("original")]
        public int? Original;

        [JsonProperty("comment")]
        public int? Comment;

        [JsonProperty("lyrics")]
        public int? Lyrics;

        [JsonProperty("karaoke")]
        public int? Karaoke;

        [JsonProperty("forced")]
        public int? Forced;

        [JsonProperty("hearing_impaired")]
        public int? HearingImpaired;

        [JsonProperty("visual_impaired")]
        public int? VisualImpaired;

        [JsonProperty("clean_effects")]
        public int? CleanEffects;

        [JsonProperty("attached_pic")]
        public int? AttachedPic;

        [JsonProperty("timed_thumbnails")]
        public int? TimedThumbnails;

        [JsonProperty("non_diegetic")]
        public int? NonDiegetic;

        [JsonProperty("captions")]
        public int? Captions;

        [JsonProperty("descriptions")]
        public int? Descriptions;

        [JsonProperty("metadata")]
        public int? Metadata;

        [JsonProperty("dependent")]
        public int? Dependent;

        [JsonProperty("still_image")]
        public int? StillImage;

        [JsonProperty("multilayer")]
        public int? Multilayer;
    }

    public class FFProbeMetadataInfo
    {
        [JsonProperty("streams")]
        public List<Stream>? Streams;
    }

    public class Stream
    {
        [JsonProperty("index")]
        public int? Index;

        [JsonProperty("codec_name")]
        public string? CodecName;

        [JsonProperty("codec_long_name")]
        public string? CodecLongName;

        [JsonProperty("profile")]
        public string? Profile;

        [JsonProperty("codec_type")]
        public string? CodecType;

        [JsonProperty("codec_tag_string")]
        public string? CodecTagString;

        [JsonProperty("codec_tag")]
        public string? CodecTag;

        [JsonProperty("mime_codec_string")]
        public string? MimeCodecString;

        [JsonProperty("width")]
        public int? Width;

        [JsonProperty("height")]
        public int? Height;

        [JsonProperty("coded_width")]
        public int? CodedWidth;

        [JsonProperty("coded_height")]
        public int? CodedHeight;

        [JsonProperty("has_b_frames")]
        public int? HasBFrames;

        [JsonProperty("sample_aspect_ratio")]
        public string? SampleAspectRatio;

        [JsonProperty("display_aspect_ratio")]
        public string? DisplayAspectRatio;

        [JsonProperty("pix_fmt")]
        public string? PixFmt;

        [JsonProperty("level")]
        public int? Level;

        [JsonProperty("color_range")]
        public string? ColorRange;

        [JsonProperty("color_space")]
        public string? ColorSpace;

        [JsonProperty("color_transfer")]
        public string? ColorTransfer;

        [JsonProperty("color_primaries")]
        public string? ColorPrimaries;

        [JsonProperty("chroma_location")]
        public string? ChromaLocation;

        [JsonProperty("is_avc")]
        public string? IsAvc;

        [JsonProperty("nal_length_size")]
        public string? NalLengthSize;

        [JsonProperty("id")]
        public string? Id;

        [JsonProperty("r_frame_rate")]
        public string? RFrameRate;

        [JsonProperty("avg_frame_rate")]
        public string? AvgFrameRate;

        [JsonProperty("time_base")]
        public string? TimeBase;

        [JsonProperty("start_pts")]
        public int? StartPts;

        [JsonProperty("start_time")]
        public string? StartTime;

        [JsonProperty("bits_per_raw_sample")]
        public string? BitsPerRawSample;

        [JsonProperty("extradata_size")]
        public int? ExtradataSize;

        [JsonProperty("disposition")]
        public Disposition? Disposition;

        [JsonProperty("tags")]
        public Tags? Tags;

        [JsonProperty("sample_fmt")]
        public string? SampleFmt;

        [JsonProperty("sample_rate")]
        public string? SampleRate;

        [JsonProperty("channels")]
        public int? Channels;

        [JsonProperty("channel_layout")]
        public string? ChannelLayout;

        [JsonProperty("bits_per_sample")]
        public int? BitsPerSample;

        [JsonProperty("initial_padding")]
        public int? InitialPadding;

        [JsonProperty("duration_ts")]
        public int? DurationTs;

        [JsonProperty("duration")]
        public string? Duration;
    }

    public class Tags
    {
        [JsonProperty("variant_bitrate")]
        public string? VariantBitrate;
    }
}
