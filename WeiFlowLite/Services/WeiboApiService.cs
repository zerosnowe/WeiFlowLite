using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WeiFlowLite.Models;

namespace WeiFlowLite.Services
{
    public sealed class WeiboApiService
    {
        private const string TimelineUrl = "https://m.weibo.cn/api/container/getIndex";
        private const string DetailUrl = "https://m.weibo.cn/statuses/show";
        private const string FriendsTimelineUrl = "https://m.weibo.cn/feed/friends";
        private const string CommentsUrl = "https://m.weibo.cn/comments/hotflow";
        private const string CreateCommentUrl = "https://m.weibo.cn/api/comments/create";
        private const string LikeUrl = "https://m.weibo.cn/api/attitudes/create";
        private const string UnlikeUrl = "https://m.weibo.cn/api/attitudes/destroy";
        private const string FavoriteUrl = "https://m.weibo.cn/api/favorites/create";
        private const string UnfavoriteUrl = "https://m.weibo.cn/api/favorites/destroy";
        private const string FollowUserUrl = "https://m.weibo.cn/api/friendships/create";
        private const string UnfollowUserUrl = "https://m.weibo.cn/api/friendships/destroy";
        private const string HotSearchUrl = "https://weibo.com/ajax/statuses/hot_band";
        private const string SearchUrl = "https://m.weibo.cn/api/container/getIndex";
        private const string ConfigUrl = "https://m.weibo.cn/api/config/list";
        private const string ClientConfigUrl = "https://m.weibo.cn/api/config";
        private const string VisitorUrl = "https://visitor.passport.weibo.cn/visitor/genvisitor2";
        private const string VisitorPageUrl = "https://visitor.passport.weibo.cn/visitor/visitor";
        private const string HomeUrl = "https://m.weibo.cn/";
        private const string MobileWeiboHost = "m.weibo.cn";
        private const string ContainerId = "102803";

        private readonly CookieContainer _cookies;
        private readonly HttpClient _httpClient;
        private bool _hasVisitorSession;
        private bool _hasAuthenticatedSession;
        private string _stToken;

        public bool HasAuthenticatedSession => _hasAuthenticatedSession;

        public WeiboApiService()
        {
            _cookies = new CookieContainer();
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = _cookies,
                UseCookies = true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        }

        public async Task<List<WeiboMblog>> GetTimelineAsync(long sinceId = 0)
        {
            var page = await GetTimelinePageAsync(sinceId);
            return page.Mblogs;
        }

        public void ApplyStoredCookies(IEnumerable<WeiboStoredCookie> cookies)
        {
            var usefulCookies = (cookies ?? Enumerable.Empty<WeiboStoredCookie>())
                .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Name) &&
                                 !string.IsNullOrWhiteSpace(cookie.Value) &&
                                 !string.IsNullOrWhiteSpace(cookie.Domain))
                .ToList();

            if (usefulCookies.Count == 0)
            {
                ClearAuthentication();
                return;
            }

            foreach (var storedCookie in usefulCookies)
            {
                var domain = storedCookie.Domain.TrimStart('.');
                var path = string.IsNullOrWhiteSpace(storedCookie.Path) ? "/" : storedCookie.Path;
                try
                {
                    _cookies.Add(new Uri($"https://{domain}/"), new Cookie(storedCookie.Name, storedCookie.Value, path, domain));
                }
                catch
                {
                    continue;
                }
            }

            _hasAuthenticatedSession = HasMobileAuthenticationCookie();
            _hasVisitorSession = false;
            _stToken = null;
        }

        public void ClearAuthentication()
        {
            _hasAuthenticatedSession = false;
            _hasVisitorSession = false;
            _stToken = null;
        }

        public async Task<WeiboTimelinePage> GetTimelinePageAsync(long sinceId = 0)
        {
            return await GetTimelinePageAsync(ContainerId, sinceId);
        }

        public async Task<WeiboTimelinePage> GetTimelinePageAsync(
            string containerId,
            long sinceId = 0,
            double? latitude = null,
            double? longitude = null)
        {
            await EnsureVisitorSessionAsync();

            if (string.IsNullOrWhiteSpace(containerId))
            {
                containerId = ContainerId;
            }

            var url = $"{TimelineUrl}?containerid={Uri.EscapeDataString(containerId)}";
            if (sinceId > 0)
            {
                url += $"&since_id={sinceId}";
            }

            if (latitude.HasValue && longitude.HasValue)
            {
                url += "&lat=" + latitude.Value.ToString("F6", CultureInfo.InvariantCulture);
                url += "&long=" + longitude.Value.ToString("F6", CultureInfo.InvariantCulture);
            }

            var response = await GetTimelineResponseAsync(url);
            if (IsRedirect(response.StatusCode))
            {
                response.Dispose();
                _hasVisitorSession = false;
                await EnsureVisitorSessionAsync();
                response = await GetTimelineResponseAsync(url);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith("<"))
                {
                    throw new InvalidOperationException("微博没有返回 JSON；匿名访客身份已失效或当前网络被限流。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboApiResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var mblogs = apiResponse.Data?.Cards?
                    .Where(card => card.CardType == 9 && card.Mblog != null)
                    .Select(card => card.Mblog)
                    .ToList() ?? new List<WeiboMblog>();
                var nextSinceId = apiResponse.Data?.CardlistInfo?.SinceId ?? 0;
                return new WeiboTimelinePage(mblogs, nextSinceId);
            }
        }

        public async Task<List<WeiboChannel>> GetChannelsAsync()
        {
            await EnsureVisitorSessionAsync();

            var response = await GetTimelineResponseAsync(ConfigUrl);
            if (IsRedirect(response.StatusCode))
            {
                response.Dispose();
                _hasVisitorSession = false;
                await EnsureVisitorSessionAsync();
                response = await GetTimelineResponseAsync(ConfigUrl);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博分类接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboConfigResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博分类接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                return apiResponse.Data?.Channel?
                    .Where(channel => !string.IsNullOrWhiteSpace(channel.Gid) && !string.IsNullOrWhiteSpace(channel.Name))
                    .ToList() ?? new List<WeiboChannel>();
            }
        }

        public async Task<WeiboTimelinePage> GetFriendsTimelinePageAsync(string cursor = null)
        {
            if (!_hasAuthenticatedSession)
            {
                throw new InvalidOperationException("关注时间线需要 m.weibo.cn 域的登录状态，请重新登录后再试。");
            }

            var maxId = string.IsNullOrWhiteSpace(cursor) ? "0" : cursor;
            var url = $"{FriendsTimelineUrl}?max_id={Uri.EscapeDataString(maxId)}&max_id_type=0";

            var response = await GetTimelineResponseAsync(url);
            if (IsRedirect(response.StatusCode))
            {
                response.Dispose();
                _hasAuthenticatedSession = false;
                throw new InvalidOperationException("微博移动站登录状态已失效，请重新登录后再试。");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _hasAuthenticatedSession = false;
                    }
                    throw new HttpRequestException($"微博关注接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboFriendsTimelineResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    _hasAuthenticatedSession = false;
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博关注接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var mblogs = apiResponse.Data?.Statuses?
                    .Where(mblog => mblog != null)
                    .ToList();
                var root = JsonConvert.DeserializeObject<JObject>(body);
                if (mblogs == null)
                {
                    mblogs = root?.SelectTokens("data.cards[*].mblog")
                        .Select(token => token.ToObject<WeiboMblog>())
                        .Where(mblog => mblog != null)
                        .ToList() ?? new List<WeiboMblog>();
                }

                var nextCursor = apiResponse.Data?.NextCursor ?? root?.SelectToken("data.cardlistInfo.since_id")?.ToString();
                if (string.IsNullOrWhiteSpace(nextCursor))
                {
                    nextCursor = apiResponse.Data?.MaxId ?? root?.SelectToken("data.max_id")?.ToString();
                }

                return new WeiboTimelinePage(mblogs, 0, nextCursor);
            }
        }

        public async Task<WeiboMblog> GetMblogDetailAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("微博 id 不能为空。", nameof(id));
            }

            await EnsureVisitorSessionAsync();

            var url = $"{DetailUrl}?id={Uri.EscapeDataString(id)}";
            var response = await GetTimelineResponseAsync(url);
            if (IsRedirect(response.StatusCode))
            {
                response.Dispose();
                _hasVisitorSession = false;
                await EnsureVisitorSessionAsync();
                response = await GetTimelineResponseAsync(url);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博详情接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboDetailResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1 || apiResponse.Data == null)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博详情接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                return apiResponse.Data;
            }
        }

        public async Task<WeiboUserProfile> GetUserProfileAsync(long uid)
        {
            if (uid <= 0)
            {
                throw new ArgumentException("用户 id 不能为空。", nameof(uid));
            }

            await EnsureVisitorSessionAsync();
            var url = $"{TimelineUrl}?type=uid&value={uid}";
            using (var response = await GetTimelineResponseAsync(url))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博用户资料接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var root = JsonConvert.DeserializeObject<JObject>(body);
                if (root == null || root.Value<int?>("ok") != 1)
                {
                    var message = root?.Value<string>("msg") ?? TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博用户资料接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var userToken = root["data"]?["userInfo"] ?? root.SelectToken("$..userInfo");
                var user = userToken?.ToObject<WeiboUser>();
                if (user == null)
                {
                    throw new InvalidOperationException("微博没有返回用户资料。");
                }

                return new WeiboUserProfile { User = user };
            }
        }

        public async Task<WeiboUser> GetCurrentUserAsync()
        {
            await EnsureVisitorSessionAsync();
            using (var response = await GetTimelineResponseAsync(ClientConfigUrl))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博当前用户接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var root = JsonConvert.DeserializeObject<JObject>(body);
                var userToken = root?["data"]?["user"] ??
                                root?["data"]?["userInfo"] ??
                                root?.SelectToken("$..userInfo");
                return userToken?.ToObject<WeiboUser>();
            }
        }

        public async Task<WeiboTimelinePage> GetUserTimelinePageAsync(long uid, long sinceId = 0)
        {
            if (uid <= 0)
            {
                throw new ArgumentException("用户 id 不能为空。", nameof(uid));
            }

            return await GetTimelinePageAsync($"107603{uid}", sinceId);
        }

        public async Task<WeiboCommentsPage> GetCommentsPageAsync(string id, string maxId = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("微博 id 不能为空。", nameof(id));
            }

            await EnsureVisitorSessionAsync();

            var url = $"{CommentsUrl}?id={Uri.EscapeDataString(id)}&mid={Uri.EscapeDataString(id)}&max_id_type=0";
            if (!string.IsNullOrWhiteSpace(maxId) && maxId != "0")
            {
                url += $"&max_id={Uri.EscapeDataString(maxId)}";
            }
            using (var response = await GetTimelineResponseAsync(url))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博评论接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboCommentsResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博评论接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var comments = apiResponse.Data?.Comments?
                    .Where(comment => comment != null)
                    .ToList() ?? new List<WeiboComment>();
                return new WeiboCommentsPage(comments, apiResponse.Data?.MaxId);
            }
        }

        public async Task<List<WeiboComment>> GetCommentsAsync(string id)
        {
            var page = await GetCommentsPageAsync(id);
            return page.Comments;
        }

        public async Task PostCommentAsync(string id, string content)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("微博 id 不能为空。", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("评论内容不能为空。", nameof(content));
            }

            await EnsureVisitorSessionAsync();
            var st = await GetStTokenAsync();
            var form =
                $"content={Uri.EscapeDataString(content)}" +
                $"&mid={Uri.EscapeDataString(id)}" +
                $"&id={Uri.EscapeDataString(id)}" +
                $"&st={Uri.EscapeDataString(st)}" +
                "&_spr=screen%3A1920x1080";

            using (var response = await PostTimelineFormAsync(CreateCommentUrl, form))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博评论发布失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<JObject>(body);
                if (apiResponse == null || apiResponse.Value<int?>("ok") != 1)
                {
                    var message = apiResponse?.Value<string>("msg") ?? TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博评论发布失败{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }
            }
        }

        public async Task SetLikeAsync(string id, bool like)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("微博 id 不能为空。", nameof(id));
            }

            await EnsureVisitorSessionAsync();
            var st = await GetStTokenAsync();
            var form =
                $"id={Uri.EscapeDataString(id)}" +
                $"&st={Uri.EscapeDataString(st)}" +
                "&_spr=screen%3A1920x1080";
            using (var response = await PostTimelineFormAsync(like ? LikeUrl : UnlikeUrl, form))
            {
                var body = await response.Content.ReadAsStringAsync();
                EnsureMutationSucceeded(response, body, like ? "点赞失败" : "取消点赞失败");
            }
        }

        public async Task SetFavoriteAsync(string id, bool favorite)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("微博 id 不能为空。", nameof(id));
            }

            await EnsureVisitorSessionAsync();
            var st = await GetStTokenAsync();
            var form =
                $"id={Uri.EscapeDataString(id)}" +
                $"&st={Uri.EscapeDataString(st)}" +
                "&_spr=screen%3A1920x1080";
            using (var response = await PostTimelineFormAsync(favorite ? FavoriteUrl : UnfavoriteUrl, form))
            {
                var body = await response.Content.ReadAsStringAsync();
                EnsureMutationSucceeded(response, body, favorite ? "收藏失败" : "取消收藏失败");
            }
        }

        public async Task SetFollowAsync(long uid, bool follow)
        {
            if (uid <= 0)
            {
                throw new ArgumentException("用户 id 不能为空。", nameof(uid));
            }

            await EnsureVisitorSessionAsync();
            var st = await GetStTokenAsync();
            var form =
                $"uid={uid}" +
                $"&st={Uri.EscapeDataString(st)}" +
                "&_spr=screen%3A1920x1080";
            using (var response = await PostTimelineFormAsync(follow ? FollowUserUrl : UnfollowUserUrl, form))
            {
                var body = await response.Content.ReadAsStringAsync();
                EnsureMutationSucceeded(response, body, follow ? "关注失败" : "取消关注失败");
            }
        }

        public async Task<List<WeiboEmojiGroup>> GetEmojiGroupsAsync()
        {
            await EnsureVisitorSessionAsync();
            try
            {
                using (var response = await GetTimelineResponseAsync(ClientConfigUrl))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var groups = ParseEmojiGroups(body);
                        if (groups.Count > 0)
                        {
                            return groups;
                        }
                    }
                }
            }
            catch
            {
            }

            return GetFallbackEmojiGroups();
        }

        public async Task<List<WeiboHotSearchItem>> GetHotSearchAsync()
        {
            await EnsureVisitorSessionAsync();

            using (var response = await GetWebResponseAsync(HotSearchUrl))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博热搜接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboHotSearchResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博热搜接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var items = apiResponse.Data?.BandList ?? apiResponse.Data?.Realtime;
                return items?
                    .Where(item => item != null && (!string.IsNullOrWhiteSpace(item.Note) || !string.IsNullOrWhiteSpace(item.Word)))
                    .ToList() ?? new List<WeiboHotSearchItem>();
            }
        }

        public async Task<WeiboTimelinePage> SearchPageAsync(string keyword, long sinceId = 0)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return new WeiboTimelinePage(new List<WeiboMblog>(), 0);
            }

            await EnsureVisitorSessionAsync();

            var containerId = $"100103type=1&q={keyword}";
            var url = $"{SearchUrl}?containerid={Uri.EscapeDataString(containerId)}";
            if (sinceId > 0)
            {
                url += $"&since_id={sinceId}";
            }
            using (var response = await GetTimelineResponseAsync(url))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"微博搜索接口请求失败 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var apiResponse = JsonConvert.DeserializeObject<WeiboApiResponse>(body);
                if (apiResponse == null || apiResponse.Ok != 1)
                {
                    var message = TryGetApiMessage(body);
                    throw new InvalidOperationException($"微博搜索接口返回错误{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
                }

                var mblogs = apiResponse.Data?.Cards?
                    .Where(card => card.CardType == 9 && card.Mblog != null)
                    .Select(card => card.Mblog)
                    .ToList() ?? new List<WeiboMblog>();
                var nextSinceId = apiResponse.Data?.CardlistInfo?.SinceId ?? 0;
                return new WeiboTimelinePage(mblogs, nextSinceId);
            }
        }

        public async Task<List<WeiboMblog>> SearchAsync(string keyword)
        {
            var page = await SearchPageAsync(keyword);
            return page.Mblogs;
        }

        private async Task<HttpResponseMessage> GetTimelineResponseAsync(string url)
        {
            return await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri(HomeUrl);
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                return request;
            });
        }

        private async Task<HttpResponseMessage> GetWebResponseAsync(string url)
        {
            return await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri("https://weibo.com/");
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                return request;
            });
        }

        private async Task<HttpResponseMessage> PostTimelineFormAsync(string url, string form)
        {
            return await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Referrer = new Uri(HomeUrl);
                request.Headers.TryAddWithoutValidation("Origin", "https://m.weibo.cn");
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                request.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");
                return request;
            });
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> createRequest)
        {
            Exception lastException = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    var response = await _httpClient.SendAsync(createRequest());
                    if (!ShouldRetry(response.StatusCode) || attempt == 5)
                    {
                        return response;
                    }

                    response.Dispose();
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    if (attempt == 5)
                    {
                        throw;
                    }
                }
                catch (TaskCanceledException ex)
                {
                    lastException = ex;
                    if (attempt == 5)
                    {
                        throw new HttpRequestException("微博接口请求超时。", ex);
                    }
                }

                await Task.Delay(350 * attempt);
            }

            throw new HttpRequestException("微博接口请求失败。", lastException);
        }

        private async Task EnsureVisitorSessionAsync()
        {
            if (_hasAuthenticatedSession)
            {
                return;
            }

            if (_hasVisitorSession)
            {
                return;
            }

            await EstablishVisitorSessionAsync();
            _hasVisitorSession = true;
        }

        private bool HasMobileAuthenticationCookie()
        {
            return _cookies.GetCookies(new Uri(HomeUrl))
                .Cast<Cookie>()
                .Any(cookie => string.Equals(cookie.Name, "SUB", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(cookie.Value) &&
                               IsCookieApplicableToHost(cookie.Domain, MobileWeiboHost));
        }

        private static bool IsCookieApplicableToHost(string cookieDomain, string host)
        {
            if (string.IsNullOrWhiteSpace(cookieDomain) || string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var normalizedDomain = cookieDomain.Trim().TrimStart('.');
            return string.Equals(host, normalizedDomain, StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith("." + normalizedDomain, StringComparison.OrdinalIgnoreCase);
        }

        private async Task EstablishVisitorSessionAsync()
        {
            var requestId = Guid.NewGuid().ToString("N");
            var rid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var form =
                "cb=visitor_gray_callback" +
                "&ver=20250916" +
                $"&request_id={requestId}" +
                "&tid=" +
                "&from=weibo" +
                "&webdriver=false" +
                $"&rid={rid}" +
                "&return_url=https%3A%2F%2Fm.weibo.cn%2F";

            using (var request = new HttpRequestMessage(HttpMethod.Post, VisitorUrl))
            {
                request.Headers.Referrer = new Uri(VisitorPageUrl);
                request.Headers.TryAddWithoutValidation("Origin", "https://visitor.passport.weibo.cn");
                request.Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");

                using (var response = await _httpClient.SendAsync(request))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"无法建立微博访客会话 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                    }

                    var visitorData = ParseJsonp(body);
                    if (visitorData.Value<int?>("retcode") != 20000000)
                    {
                        throw new InvalidOperationException($"微博访客会话被拒绝: {visitorData.Value<string>("msg") ?? "未知错误"}。");
                    }
                }
            }

            var weiboCookies = _cookies.GetCookies(new Uri(HomeUrl)).Cast<Cookie>();
            if (!weiboCookies.Any(cookie => cookie.Name == "SUB" && !string.IsNullOrEmpty(cookie.Value)))
            {
                throw new InvalidOperationException("微博访客会话未返回 SUB Cookie。");
            }
        }

        private static JObject ParseJsonp(string jsonp)
        {
            var match = Regex.Match(jsonp ?? string.Empty, @"\(\s*(\{.*\})\s*\)\s*;?\s*$", RegexOptions.Singleline);
            if (!match.Success)
            {
                throw new InvalidOperationException("微博访客接口返回了无法识别的内容。");
            }

            return JObject.Parse(match.Groups[1].Value);
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.Moved ||
                   statusCode == HttpStatusCode.Redirect ||
                   statusCode == HttpStatusCode.RedirectMethod ||
                   (int)statusCode == 307 ||
                   (int)statusCode == 308;
        }

        private static string TryGetApiMessage(string body)
        {
            try
            {
                return JObject.Parse(body).Value<string>("msg");
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void EnsureMutationSucceeded(HttpResponseMessage response, string body, string failureTitle)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{failureTitle} ({(int)response.StatusCode} {response.ReasonPhrase})。");
            }

            var apiResponse = JsonConvert.DeserializeObject<JObject>(body);
            if (apiResponse == null || apiResponse.Value<int?>("ok") != 1)
            {
                var message = apiResponse?.Value<string>("msg") ?? TryGetApiMessage(body);
                throw new InvalidOperationException($"{failureTitle}{(string.IsNullOrEmpty(message) ? string.Empty : $": {message}")}。");
            }
        }

        private async Task<string> GetStTokenAsync()
        {
            if (!string.IsNullOrWhiteSpace(_stToken))
            {
                return _stToken;
            }

            using (var response = await GetTimelineResponseAsync(ClientConfigUrl))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"无法获取微博发布令牌 ({(int)response.StatusCode} {response.ReasonPhrase})。");
                }

                var config = JsonConvert.DeserializeObject<JObject>(body);
                _stToken = config?["data"]?.Value<string>("st") ?? config?.Value<string>("st");
                if (string.IsNullOrWhiteSpace(_stToken))
                {
                    throw new InvalidOperationException("微博没有返回评论发布令牌，请重新登录后再试。");
                }

                return _stToken;
            }
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == (HttpStatusCode)429 ||
                   code >= 500;
        }

        private static List<WeiboEmojiGroup> ParseEmojiGroups(string body)
        {
            var root = JsonConvert.DeserializeObject<JObject>(body);
            if (root == null)
            {
                return new List<WeiboEmojiGroup>();
            }

            var tokens = root.SelectTokens("$..emoticon")
                .Concat(root.SelectTokens("$..emotions"))
                .Concat(root.SelectTokens("$..emoji"))
                .Where(token => token.Type == JTokenType.Array)
                .ToList() ?? new List<JToken>();
            var groups = new List<WeiboEmojiGroup>();
            foreach (var token in tokens)
            {
                var items = token.Children()
                    .Select(item => new WeiboEmojiItem
                    {
                        Phrase = item.Value<string>("phrase") ?? item.Value<string>("value") ?? item.Value<string>("name"),
                        Url = item.Value<string>("url") ?? item.Value<string>("icon")
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Phrase))
                    .GroupBy(item => item.Phrase)
                    .Select(group => group.First())
                    .Take(48)
                    .ToList();
                if (items.Count > 0)
                {
                    groups.Add(new WeiboEmojiGroup { Name = groups.Count == 0 ? "默认" : $"表情 {groups.Count + 1}", Items = items });
                }
            }

            return groups;
        }

        private static List<WeiboEmojiGroup> GetFallbackEmojiGroups()
        {
            return new List<WeiboEmojiGroup>
            {
                new WeiboEmojiGroup
                {
                    Name = "常用",
                    Items = new List<WeiboEmojiItem>
                    {
                        new WeiboEmojiItem { Phrase = "[微笑]" },
                        new WeiboEmojiItem { Phrase = "[嘻嘻]" },
                        new WeiboEmojiItem { Phrase = "[哈哈]" },
                        new WeiboEmojiItem { Phrase = "[可爱]" },
                        new WeiboEmojiItem { Phrase = "[爱你]" },
                        new WeiboEmojiItem { Phrase = "[泪]" },
                        new WeiboEmojiItem { Phrase = "[允悲]" },
                        new WeiboEmojiItem { Phrase = "[doge]" }
                    }
                },
                new WeiboEmojiGroup
                {
                    Name = "态度",
                    Items = new List<WeiboEmojiItem>
                    {
                        new WeiboEmojiItem { Phrase = "[赞]" },
                        new WeiboEmojiItem { Phrase = "[鼓掌]" },
                        new WeiboEmojiItem { Phrase = "[作揖]" },
                        new WeiboEmojiItem { Phrase = "[抱抱]" },
                        new WeiboEmojiItem { Phrase = "[跪了]" },
                        new WeiboEmojiItem { Phrase = "[摊手]" },
                        new WeiboEmojiItem { Phrase = "[并不简单]" },
                        new WeiboEmojiItem { Phrase = "[二哈]" }
                    }
                }
            };
        }
    }

    public sealed class WeiboTimelinePage
    {
        public List<WeiboMblog> Mblogs { get; }
        public long NextSinceId { get; }
        public string NextCursor { get; }

        public WeiboTimelinePage(List<WeiboMblog> mblogs, long nextSinceId, string nextCursor = null)
        {
            Mblogs = mblogs;
            NextSinceId = nextSinceId;
            NextCursor = nextCursor;
        }
    }

    public sealed class WeiboCommentsPage
    {
        public List<WeiboComment> Comments { get; }
        public string NextMaxId { get; }

        public WeiboCommentsPage(List<WeiboComment> comments, string nextMaxId)
        {
            Comments = comments;
            NextMaxId = nextMaxId;
        }
    }
}
