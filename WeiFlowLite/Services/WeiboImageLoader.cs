using System;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace WeiFlowLite.Services
{
    public sealed class WeiboImageLoader
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<ImageSource> LoadAsync(string url, int decodePixelWidth)
        {
            Uri imageUri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out imageUri) ||
                !string.Equals(imageUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using (var response = await HttpClient.GetAsync(imageUri))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                if (imageBytes.Length == 0)
                {
                    return null;
                }

                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(imageBytes.AsBuffer());
                    stream.Seek(0);

                    var image = new BitmapImage();
                    if (decodePixelWidth > 0)
                    {
                        image.DecodePixelWidth = decodePixelWidth;
                    }

                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri("https://m.weibo.cn/");
            return client;
        }
    }
}