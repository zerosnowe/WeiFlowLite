using System.Collections.Generic;
using Newtonsoft.Json;

namespace WeiFlowLite.Models
{
    public class WeiboApiResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboData Data { get; set; }
    }

    public class WeiboDetailResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboMblog Data { get; set; }
    }

    public class WeiboConfigResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboConfigData Data { get; set; }
    }

    public class WeiboFriendsTimelineResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboFriendsTimelineData Data { get; set; }
    }

    public class WeiboFriendsTimelineData
    {
        [JsonProperty("statuses")]
        public List<WeiboMblog> Statuses { get; set; }

        [JsonProperty("max_id")]
        public string MaxId { get; set; }

        [JsonProperty("next_cursor")]
        public string NextCursor { get; set; }
    }

    public class WeiboCommentsResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboCommentsData Data { get; set; }
    }

    public class WeiboCommentsData
    {
        [JsonProperty("data")]
        public List<WeiboComment> Comments { get; set; }

        [JsonProperty("max_id")]
        public string MaxId { get; set; }
    }

    public class WeiboComment
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("like_counts")]
        public int LikeCounts { get; set; }

        [JsonProperty("user")]
        public WeiboUser User { get; set; }
    }

    public class WeiboHotSearchResponse
    {
        [JsonProperty("ok")]
        public int Ok { get; set; }

        [JsonProperty("data")]
        public WeiboHotSearchData Data { get; set; }
    }

    public class WeiboHotSearchData
    {
        [JsonProperty("realtime")]
        public List<WeiboHotSearchItem> Realtime { get; set; }

        [JsonProperty("band_list")]
        public List<WeiboHotSearchItem> BandList { get; set; }
    }

    public class WeiboHotSearchItem
    {
        [JsonProperty("note")]
        public string Note { get; set; }

        [JsonProperty("word")]
        public string Word { get; set; }

        [JsonProperty("num")]
        public long Num { get; set; }

        [JsonProperty("label_name")]
        public string LabelName { get; set; }

        [JsonProperty("small_icon_desc")]
        public string SmallIconDesc { get; set; }
    }

    public class WeiboEmojiGroup
    {
        public string Name { get; set; }

        public List<WeiboEmojiItem> Items { get; set; }

        public override string ToString()
        {
            return Name ?? "表情";
        }
    }

    public class WeiboEmojiItem
    {
        public string Phrase { get; set; }

        public string Url { get; set; }

        public override string ToString()
        {
            return Phrase ?? string.Empty;
        }
    }

    public class WeiboConfigData
    {
        [JsonProperty("channel")]
        public List<WeiboChannel> Channel { get; set; }
    }

    public class WeiboChannel
    {
        [JsonProperty("gid")]
        public string Gid { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class WeiboData
    {
        [JsonProperty("cardlistInfo")]
        public CardlistInfo CardlistInfo { get; set; }

        [JsonProperty("cards")]
        public List<WeiboCard> Cards { get; set; }
    }

    public class CardlistInfo
    {
        [JsonProperty("since_id")]
        public long SinceId { get; set; }
    }

    public class WeiboCard
    {
        [JsonProperty("card_type")]
        public int CardType { get; set; }

        [JsonProperty("mblog")]
        public WeiboMblog Mblog { get; set; }
    }

    public class WeiboMblog
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("user")]
        public WeiboUser User { get; set; }

        [JsonProperty("pics")]
        public List<WeiboPic> Pics { get; set; }

        [JsonProperty("reposts_count")]
        public int RepostsCount { get; set; }

        [JsonProperty("comments_count")]
        public int CommentsCount { get; set; }

        [JsonProperty("attitudes_count")]
        public int AttitudesCount { get; set; }

        [JsonProperty("attitudes_status")]
        public int AttitudesStatus { get; set; }

        [JsonProperty("favorited")]
        public bool Favorited { get; set; }

        [JsonProperty("page_info")]
        public WeiboPageInfo PageInfo { get; set; }
    }

    public class WeiboUser
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("screen_name")]
        public string ScreenName { get; set; }

        [JsonProperty("profile_image_url")]
        public string ProfileImageUrl { get; set; }

        [JsonProperty("avatar_hd")]
        public string AvatarHd { get; set; }

        [JsonProperty("verified")]
        public bool Verified { get; set; }

        [JsonProperty("verified_reason")]
        public string VerifiedReason { get; set; }

        [JsonProperty("followers_count")]
        public string FollowersCount { get; set; }

        [JsonProperty("follow_count")]
        public string FollowCount { get; set; }

        [JsonProperty("statuses_count")]
        public string StatusesCount { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("following")]
        public bool Following { get; set; }
    }

    public class WeiboUserProfile
    {
        public WeiboUser User { get; set; }
    }

    public class WeiboPic
    {
        [JsonProperty("pid")]
        public string Pid { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("large")]
        public WeiboPicSize Large { get; set; }
    }

    public class WeiboPicSize
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class WeiboPageInfo
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("page_pic")]
        public WeiboPagePic PagePic { get; set; }

        [JsonProperty("media_info")]
        public WeiboMediaInfo MediaInfo { get; set; }
    }

    public class WeiboPagePic
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class WeiboMediaInfo
    {
        [JsonProperty("stream_url")]
        public string StreamUrl { get; set; }

        [JsonProperty("stream_url_hd")]
        public string StreamUrlHd { get; set; }

        [JsonProperty("mp4_sd_url")]
        public string Mp4SdUrl { get; set; }

        [JsonProperty("mp4_hd_url")]
        public string Mp4HdUrl { get; set; }

        [JsonProperty("mp4_720p_mp4")]
        public string Mp4720pMp4 { get; set; }

        [JsonProperty("mp4_1080p_mp4")]
        public string Mp41080pMp4 { get; set; }

        [JsonProperty("h5_url")]
        public string H5Url { get; set; }
    }
}
