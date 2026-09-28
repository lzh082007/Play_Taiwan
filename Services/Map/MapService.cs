// 檔案路徑：System\Services\Map\MapService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;

namespace backend.Services
{
    public class MapService
    {
        // 抵達判定距離，與任務作答共用（PlayRules）
        private const double UnlockRadiusMeters = PlayRules.ArrivalRadiusMeters;

        private readonly MapDao _dao;
        private readonly GeocodingService _geocodingService;
        private readonly PlaceLookupService _placeLookup;

        public MapService(MapDao dao, GeocodingService geocodingService, PlaceLookupService placeLookup)
        {
            _dao = dao;
            _geocodingService = geocodingService;
            _placeLookup = placeLookup;
        }

        #region 取得地圖

        /// <summary>
        /// 取得指定劇本的地圖資訊。只有劇本擁有者或協作隊員可以查看。
        /// 後端依 story_session.ss_current 判斷節點是否解鎖，座標依節點的 place_id 向 Neo4j 查。
        /// 新資料表沒有 day_index，目前所有節點都歸在第一日。
        /// </summary>
        /// <param name="storyId">劇本代號，對應 story.s_id。</param>
        /// <param name="user">目前登入使用者 JWT Claims。</param>
        /// <returns>地圖進度、節點、明信片統計與總天數。</returns>
        public async Task<MapResponse> GetMap(
            int storyId,
            ClaimsPrincipal user)
        {
            int auId = user.GetAuId();

            List<MapNode> nodes = _dao.GetStoryNodes(storyId);

            if (nodes == null || nodes.Count == 0)
            {
                throw new KeyNotFoundException("此劇本沒有可用的地圖節點。");
            }

            if (!_dao.CanPlay(auId, storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法查看地圖。");
            }

            Dictionary<string, PlaceLookupService.PlaceInfo> places =
                await _placeLookup.GetPlacesAsync(nodes.Select(x => x.place_id));

            foreach (MapNode node in nodes)
            {
                if (node.place_id != null && places.TryGetValue(node.place_id, out PlaceLookupService.PlaceInfo place))
                {
                    node.lat = place.lat;
                    node.lng = place.lng;
                }
            }

            int currentNodeOrder = _dao.GetCurrentNodeOrder(auId, storyId);

            nodes = nodes
                .OrderBy(x => x.day_index)
                .ThenBy(x => x.node_order)
                .ToList();

            foreach (MapNode node in nodes)
            {
                // 第一個節點固定開放；抵達第 N 節點後開放第 N+1 節點（協作隊伍全隊共用進度）。
                node.is_unlocked = TeamProgress.IsUnlocked(node.node_order, currentNodeOrder);

                // 已解鎖後不再顯示迷霧文字。
                if (node.is_unlocked)
                {
                    node.fog_hint = null;
                }
                else if (string.IsNullOrWhiteSpace(node.fog_hint))
                {
                    node.fog_hint = "前方仍被迷霧籠罩，抵達前一站後即可探索。";
                }

                node.child_node_ids ??= new List<int>();
            }

            // 建立線性路線：第一站 -> 第二站 -> 第三站。
            // 未來若有分支劇情，再改成資料表設定 child_node_ids。
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                nodes[i].child_node_ids.Add(nodes[i + 1].node_id);
            }

            int totalDays = nodes.Max(x => x.day_index);

            return new MapResponse
            {
                story_id = storyId,

                unlocked_node_count = nodes.Count(x => x.is_unlocked),
                total_node_count = nodes.Count,

                postcard_unlocked_count =
                    _dao.GetUnlockedPostcardCount(auId, storyId),

                postcard_total_count =
                    _dao.GetTotalPostcardCount(storyId),

                nodes = nodes,

                // 預設進入地圖先顯示第一日。
                // 前端點第二日時，以 node.day_index == 2 過濾即可，不需要再打 API。
                day_index = 1,
                total_days = totalDays
            };
        }

        #endregion

        #region GPS 確認抵達

        /// <summary>
        /// GPS 確認抵達：只有劇本擁有者或協作隊員可以抵達，且只能抵達已解鎖的站；
        /// 距離以節點 place_id 在 Neo4j 的座標計算（與任務作答同一份座標）。
        /// </summary>
        public async Task<NodeDetailResponse> ArriveNode(
            int nodeId,
            double userLat,
            double userLng,
            ClaimsPrincipal user)
        {
            int auId = user.GetAuId();

            MapNode node = _dao.GetNodeLocation(nodeId);

            if (node == null)
            {
                throw new KeyNotFoundException("找不到指定節點。");
            }

            int storyId = _dao.GetNodeStoryId(nodeId) ?? throw new KeyNotFoundException("找不到指定節點。");

            if (!_dao.CanPlay(auId, storyId))
            {
                throw new UnauthorizedAccessException("你沒有參與這個劇本，無法抵達節點。");
            }

            // 只能抵達已解鎖的站，避免直接跳到後面的站把進度推過去
            int currentNodeOrder = _dao.GetCurrentNodeOrder(auId, storyId);

            if (!TeamProgress.IsUnlocked(node.node_order, currentNodeOrder))
            {
                throw new InvalidOperationException("這一站還沒解鎖，請先抵達前一站。");
            }

            PlaceLookupService.PlaceInfo place = await _placeLookup.GetPlaceAsync(node.place_id)
                ?? throw new InvalidOperationException("查不到此節點的景點座標，無法確認抵達。");

            double distance = CalculateDistanceMeters(
                userLat,
                userLng,
                place.lat,
                place.lng);

            if (distance > UnlockRadiusMeters)
            {
                throw new InvalidOperationException(
                    $"尚未抵達指定地點，目前距離約 {Math.Round(distance)} 公尺，"
                    + $"需在 {UnlockRadiusMeters} 公尺內。");
            }

            _dao.UnlockNode(auId, nodeId);

            return GetNodeDetail(nodeId);
        }

        #endregion

        #region 取得節點詳情

        public NodeDetailResponse GetNodeDetail(int nodeId)
        {
            NodeDetailResponse result = _dao.GetNodeDetail(nodeId);

            if (result == null)
            {
                throw new KeyNotFoundException("找不到指定節點詳情。");
            }

            result.nearby_food ??= new List<string>();

            return result;
        }

        #endregion

        #region NPC 隨機互動

        public NpcInteractionResponse GetNpcInteraction(int nodeId)
        {
            NpcInteractionResponse result =
                _dao.GetRandomNpcInteraction(nodeId);

            if (result == null)
            {
                throw new KeyNotFoundException("此節點尚未設定 NPC 互動內容。");
            }

            result.node_id = nodeId;
            result.emotion ??= "normal";
            result.skip_button_text ??= "稍後再說";

            return result;
        }

        #endregion

        #region 導航

        public async Task<NavigationResponse> GetNavigation(NavigationRequest req)
        {
            if (req == null || req.node_id <= 0)
            {
                throw new ArgumentException("node_id 不可為空白。");
            }

            MapNode node = _dao.GetNodeLocation(req.node_id);

            if (node == null)
            {
                throw new KeyNotFoundException("找不到指定導航節點。");
            }

            PlaceLookupService.PlaceInfo place = await _placeLookup.GetPlaceAsync(node.place_id)
                ?? throw new InvalidOperationException("查不到此景點的座標。");

            return new NavigationResponse
            {
                maps_deeplink_url =
                    $"https://www.google.com/maps/search/?api=1&query={place.lat},{place.lng}"
            };
        }

        #endregion

        #region 周邊好去

        public List<NearbyPlaceResponse> GetNearbyPlaces(
            int storyId,
            string category)
        {
            return _dao.GetNearbyPlaces(storyId, category ?? "");
        }

        #endregion

        #region 私有邏輯

        private double CalculateDistanceMeters(
            double lat1,
            double lng1,
            double lat2,
            double lng2)
        {
            const double earthRadiusMeters = 6371000.0;

            double latDifference = DegreesToRadians(lat2 - lat1);
            double lngDifference = DegreesToRadians(lng2 - lng1);

            double a =
                Math.Sin(latDifference / 2) *
                Math.Sin(latDifference / 2) +
                Math.Cos(DegreesToRadians(lat1)) *
                Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(lngDifference / 2) *
                Math.Sin(lngDifference / 2);

            double c = 2 * Math.Atan2(
                Math.Sqrt(a),
                Math.Sqrt(1 - a));

            return earthRadiusMeters * c;
        }

        private double DegreesToRadians(double degree)
        {
            return degree * Math.PI / 180.0;
        }

        #endregion

        #region 回報定位

        /// <summary>
        /// 接收前端回報的 GPS 座標，透過共用的 GeocodingService 反向地理編碼為縣市／鄉鎮區。
        /// </summary>
        public async Task<LocationResponse> ReportLocationAsync(LocationRequest req)
        {
            if (req == null)
            {
                throw new ArgumentException("請提供定位資料。");
            }

            if (req.lat < -90 || req.lat > 90 || req.lng < -180 || req.lng > 180)
            {
                throw new ArgumentException("經緯度超出有效範圍。");
            }

            var (cityName, districtName) =
                await _geocodingService.ResolveTaiwanAreaAsync(req.lat, req.lng);

            return new LocationResponse
            {
                lat = req.lat,
                lng = req.lng,
                accuracy = req.accuracy,
                city_name = cityName,
                district_name = districtName,
                received_at = DateTime.UtcNow
            };
        }

        #endregion
    }
}