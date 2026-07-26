using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WeiFlowLite.Models;
using Windows.Storage;

namespace WeiFlowLite.Services
{
    public sealed class LocalFavoritesService
    {
        private const string FileName = "favorites.json";
        private List<WeiboMblog> _cachedFavorites;
        private bool _loaded;

        public async Task<List<WeiboMblog>> GetAllAsync()
        {
            await EnsureLoadedAsync();
            return _cachedFavorites.ToList();
        }

        public async Task<bool> IsFavoritedAsync(string id)
        {
            await EnsureLoadedAsync();
            return _cachedFavorites.Any(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public async Task AddAsync(WeiboMblog mblog)
        {
            await EnsureLoadedAsync();
            if (_cachedFavorites.Any(f => string.Equals(f.Id, mblog.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _cachedFavorites.Insert(0, mblog);
            await SaveAsync();
        }

        public async Task RemoveAsync(string id)
        {
            await EnsureLoadedAsync();
            _cachedFavorites.RemoveAll(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
            await SaveAsync();
        }

        private async Task EnsureLoadedAsync()
        {
            if (_loaded)
            {
                return;
            }

            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName);
                var json = await FileIO.ReadTextAsync(file);
                _cachedFavorites = JsonConvert.DeserializeObject<List<WeiboMblog>>(json) ?? new List<WeiboMblog>();
            }
            catch (FileNotFoundException)
            {
                _cachedFavorites = new List<WeiboMblog>();
            }
            catch
            {
                _cachedFavorites = new List<WeiboMblog>();
            }

            _loaded = true;
        }

        private async Task SaveAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_cachedFavorites);
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch
            {
            }
        }
    }
}