using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;

namespace WeiFlowLite.Services
{
    public enum LiveTileFeedSource
    {
        Recommended = 0,
        Following = 1
    }

    public sealed class LiveTileService
    {
        private const string FeedSourceSettingKey = "LiveTileFeedSource";

        public LiveTileFeedSource GetFeedSource()
        {
            var value = ApplicationData.Current.LocalSettings.Values[FeedSourceSettingKey];
            if (value is int intValue && Enum.IsDefined(typeof(LiveTileFeedSource), intValue))
            {
                return (LiveTileFeedSource)intValue;
            }

            return LiveTileFeedSource.Recommended;
        }

        public void SetFeedSource(LiveTileFeedSource source)
        {
            ApplicationData.Current.LocalSettings.Values[FeedSourceSettingKey] = (int)source;
        }

        public void EnsureRecommendedWhenUnauthenticated(bool isAuthenticated)
        {
            if (!isAuthenticated && GetFeedSource() == LiveTileFeedSource.Following)
            {
                SetFeedSource(LiveTileFeedSource.Recommended);
            }
        }

        public void UpdateTiles(IEnumerable<WeiboItemViewModel> items)
        {
            var tileItems = (items ?? Enumerable.Empty<WeiboItemViewModel>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Text))
                .Take(5)
                .ToList();

            var updater = TileUpdateManager.CreateTileUpdaterForApplication();
            updater.EnableNotificationQueue(true);
            updater.Clear();

            foreach (var item in tileItems)
            {
                var xml = CreateTileXml(item);
                updater.Update(new TileNotification(xml)
                {
                    ExpirationTime = DateTimeOffset.Now.AddHours(2)
                });
            }
        }

        private static XmlDocument CreateTileXml(WeiboItemViewModel item)
        {
            var title = EscapeForXml(item.User?.ScreenName ?? "微博");
            var content = EscapeForXml(TrimForTile(item.Text, 96));
            var imageUrl = EscapeForXml(GetTileImageUrl(item));
            var imageMarkup = string.IsNullOrWhiteSpace(imageUrl)
                ? string.Empty
                : $"<image placement=\"background\" src=\"{imageUrl}\"/>";

            var xml = $@"
<tile>
  <visual branding=""nameAndLogo"">
    <binding template=""TileMedium"">
      {imageMarkup}
      <text hint-style=""caption"">{title}</text>
      <text hint-style=""captionSubtle"" hint-wrap=""true"">{content}</text>
    </binding>
    <binding template=""TileWide"">
      {imageMarkup}
      <text hint-style=""subtitle"">{title}</text>
      <text hint-style=""body"" hint-wrap=""true"">{content}</text>
    </binding>
    <binding template=""TileLarge"">
      {imageMarkup}
      <text hint-style=""subtitle"">{title}</text>
      <text hint-style=""body"" hint-wrap=""true"">{content}</text>
    </binding>
  </visual>
</tile>";

            var document = new XmlDocument();
            document.LoadXml(xml);
            return document;
        }

        private static string GetTileImageUrl(WeiboItemViewModel item)
        {
            var firstImage = item.Pics?.FirstOrDefault(pic => pic != null && !pic.IsVideo);
            if (!string.IsNullOrWhiteSpace(firstImage?.PreviewUrl))
            {
                return firstImage.PreviewUrl;
            }

            firstImage = item.Pics?.FirstOrDefault();
            return firstImage?.PreviewUrl;
        }

        private static string TrimForTile(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength - 1) + "…";
        }

        private static string EscapeForXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
