using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;

namespace WeiFlowLite.Services
{
    public sealed class WeiboStoredCookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Domain { get; set; }
        public string Path { get; set; }
    }

    public sealed class WeiboAuthStorage
    {
        private const string CookieFileName = "weibo_auth_cookies.dat";

        public async Task SaveAsync(IEnumerable<WeiboStoredCookie> cookies)
        {
            var usefulCookies = cookies?
                .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name) &&
                                 !string.IsNullOrWhiteSpace(cookie.Value) &&
                                 !string.IsNullOrWhiteSpace(cookie.Domain))
                .ToList() ?? new List<WeiboStoredCookie>();

            var json = JsonConvert.SerializeObject(usefulCookies);
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            var provider = new DataProtectionProvider("LOCAL=user");
            var protectedBuffer = await provider.ProtectAsync(buffer);
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                CookieFileName,
                CreationCollisionOption.ReplaceExisting);

            await FileIO.WriteBufferAsync(file, protectedBuffer);
        }

        public async Task<IReadOnlyList<WeiboStoredCookie>> LoadAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(CookieFileName);
                var protectedBuffer = await FileIO.ReadBufferAsync(file);
                var provider = new DataProtectionProvider();
                var buffer = await provider.UnprotectAsync(protectedBuffer);
                CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
                var json = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                return JsonConvert.DeserializeObject<List<WeiboStoredCookie>>(json) ?? new List<WeiboStoredCookie>();
            }
            catch
            {
                return new List<WeiboStoredCookie>();
            }
        }

        public async Task ClearAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(CookieFileName);
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
            catch
            {
            }
        }
    }
}
