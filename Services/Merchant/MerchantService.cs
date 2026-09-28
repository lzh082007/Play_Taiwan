// 檔案路徑：System\Services\Merchant\MerchantService.cs
// 商家帳號、店家資料與商家影音；原 MerchantAccountService 的功能也併入這裡。
// 商家身分一律由 Controller 從 JWT（au_id / s_id Claim）取得後傳入。
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend.dao;
using backend.Models;
using backend.Services.Neo4j;
using backend.utils;
using backend.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Services
{
    public class MerchantService
    {
        private readonly MerchantDao _dao;
        private readonly PlaceVersionChainService _placeService;
        private readonly AppSettings _appSettings;
        private readonly ILogger<MerchantService> _logger;

        public MerchantService(
            MerchantDao dao,
            PlaceVersionChainService placeService,
            IOptions<AppSettings> appSettings,
            ILogger<MerchantService> logger)
        {
            _dao = dao;
            _placeService = placeService;
            _appSettings = appSettings.Value;
            _logger = logger;
        }

        #region 商家註冊
        public async Task<MerchantRegisterResponse> RegisterAsync(MerchantRegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.auth_email) || string.IsNullOrWhiteSpace(req.auth_pswd))
            {
                throw new BadRequestException("請提供 Email 與密碼");
            }

            bool hasExistingPlace = !string.IsNullOrWhiteSpace(req.place_uid);
            bool hasNewPlace = req.new_place != null;

            if (hasExistingPlace == hasNewPlace)
            {
                throw new BadRequestException("請擇一提供 place_uid（選擇既有景點）或 new_place（建立新景點）");
            }

            if (hasNewPlace)
            {
                ValidateNewPlace(req.new_place);
            }

            string storeUid;
            if (hasExistingPlace)
            {
                PlaceCurrentInfo existing = await _placeService.GetCurrentVersionAsync(req.place_uid);
                if (existing == null)
                {
                    throw new NotFoundException($"找不到 uid={req.place_uid} 的景點，請重新搜尋");
                }
                storeUid = req.place_uid;
            }
            else
            {
                storeUid = await _placeService.CreateMerchantPlaceAsync(req.new_place, req.store_name);
            }

            var hashTool = new sha256Hash();
            string passwordHash = hashTool.getSha256(req.auth_pswd, _appSettings.hash_key);

            (int auId, int sId) = _dao.RegisterMerchant(req, passwordHash, storeUid, hasNewPlace ? req.new_place : null);

            return new MerchantRegisterResponse { au_id = auId, s_id = sId, store_uid = storeUid };
        }

        // 商家自建景點可選的分類（對應 place_type.place_category），沒帶時預設 Restaurant
        private static readonly string[] NewPlaceCategories = { "Attraction", "Restaurant", "Hotel", "Event" };
        private const string DefaultNewPlaceCategory = "Restaurant";

        /// <summary>
        /// 自建景點必須帶座標（行程規劃、地圖與任務位置驗證都靠它），座標要落在臺灣（含離島）範圍內，
        /// 避免經緯度填反；分類統一成 place_type 使用的寫法。
        /// </summary>
        private static void ValidateNewPlace(MerchantNewPlace place)
        {
            if (!place.lat.HasValue || !place.lng.HasValue)
            {
                throw new BadRequestException("建立新景點時請提供座標（new_place.lat、new_place.lng）");
            }
            if (place.lat < 21 || place.lat > 27 || place.lng < 118 || place.lng > 123)
            {
                throw new BadRequestException("new_place 的座標不在臺灣範圍內，請確認經緯度是否填反");
            }

            if (string.IsNullOrWhiteSpace(place.category))
            {
                place.category = DefaultNewPlaceCategory;
                return;
            }

            string category = NewPlaceCategories.FirstOrDefault(c => string.Equals(c, place.category.Trim(), System.StringComparison.OrdinalIgnoreCase));
            place.category = category ?? throw new BadRequestException("new_place.category 只能是 Attraction、Restaurant、Hotel 或 Event");
        }
        #endregion

        #region 商家資料
        public MerchantDetailResponse GetProfile(int sId)
        {
            MerchantDetailResponse merchant = _dao.GetById(sId);
            if (merchant == null) throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            return merchant;
        }

        /// <summary>
        /// 更新店家資料（只更新有帶值的欄位），並在 Neo4j 建立新的版本節點，讓 QR Code 掃描回傳的商家資訊跟著更新。
        /// 版本節點會寫入完整欄位，所以用更新後的店家資料組內容；電話、網站、營業時間沿用目前版本的值。
        /// 商家自建景點另外同步 MySQL 的 place_type 名稱（題型抽選與任務生成用）；行程規劃、地圖的名稱與座標直接查 Neo4j。
        /// </summary>
        public async Task UpdateProfileAsync(int sId, MerchantUpdateRequest req)
        {
            if (req.store_name != null && string.IsNullOrWhiteSpace(req.store_name))
            {
                throw new BadRequestException("店家名稱不可為空白");
            }

            if (!_dao.Update(sId, req))
            {
                throw new NotFoundException($"找不到 s_id={sId} 的商家資料");
            }

            MerchantDetailResponse store = _dao.GetById(sId);
            if (string.IsNullOrWhiteSpace(store?.store_uid))
            {
                return;
            }

            PlaceCurrentInfo current = await _placeService.GetCurrentVersionAsync(store.store_uid);
            Dictionary<string, object> previous = current?.merchant_override;

            if (IsSelfBuiltPlace(current))
            {
                _dao.SyncSelfBuiltPlace(store.store_uid, store.store_name);
            }

            var fields = new MerchantPlaceFields
            {
                name = store.store_name,
                address = store.store_address,
                description = store.store_dec,
                phone = previous?.GetValueOrDefault("phone")?.ToString(),
                website = previous?.GetValueOrDefault("website")?.ToString(),
                opening_hours = previous?.GetValueOrDefault("opening_hours")?.ToString()
            };
            await _placeService.CreateNewVersionAsync(store.store_uid, fields, "merchant", sId.ToString());
        }

        /// <summary>商家列表（管理員用）。</summary>
        public MerchantListResponse List(MerchantListQuery query)
        {
            (var items, int total) = _dao.List(query);
            return new MerchantListResponse
            {
                items = items,
                total = total,
                page = query.page,
                page_size = query.page_size
            };
        }
        #endregion

        #region 刪除商家帳號
        /// <summary>
        /// 商家整體刪除：MySQL 端在單一 Transaction 內完成（見 MerchantDao.DeleteCascade）；
        /// Neo4j 沒有跨資料庫交易保護，因此放在 MySQL commit 成功之後才執行，
        /// 失敗只記 log，不影響已經完成的 MySQL 刪除結果（此階段尚未做兩階段提交）。
        /// 刪除前先從 Neo4j 判斷是不是商家自建景點：是的話該景點不再排入行程（Neo4j 節點標成已刪除，座標保留給舊劇本）。
        /// </summary>
        public async Task DeleteAccountAsync(int sId)
        {
            MerchantDetailResponse store = _dao.GetById(sId);
            if (store == null) throw new NotFoundException($"找不到 s_id={sId} 的商家資料");

            bool selfBuiltPlace = false;
            if (!string.IsNullOrWhiteSpace(store.store_uid))
            {
                try
                {
                    selfBuiltPlace = IsSelfBuiltPlace(await _placeService.GetCurrentVersionAsync(store.store_uid));
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "無法從 Neo4j 判斷商家 s_id={SId} 的景點是否為自建景點，視為既有景點處理", sId);
                }
            }

            string storeUid = _dao.DeleteCascade(sId, selfBuiltPlace);

            if (!string.IsNullOrWhiteSpace(storeUid))
            {
                try
                {
                    await _placeService.DeleteMerchantVersionAsync(storeUid);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "商家 s_id={SId} 的 MySQL 資料已刪除，但清理 Neo4j uid={Uid} 失敗", sId, storeUid);
                }
            }
        }
        #endregion

        /// <summary>商家自建的景點在 Neo4j 身分節點帶 :MerchantPlace 標籤（政府開放資料的景點沒有）</summary>
        private static bool IsSelfBuiltPlace(PlaceCurrentInfo place)
        {
            return place?.identity_labels?.Contains("MerchantPlace") == true;
        }

        #region 已生成的影音檔案
        public List<MerchantFileItem> GetMerchantFiles(int auId)
        {
            return _dao.GetMerchantFiles(auId);
        }
        #endregion

        #region 建立商家影音專案
        public int CreateVlogTask(int auId, GenerateVlogRequest req)
        {
            return _dao.CreateVlogTask(auId, req);
        }
        #endregion

        #region 取得最後生成畫面
        public MerchantVlogResult GetVlogResult(int mmId, int auId)
        {
            return _dao.GetVlogResult(mmId, auId);
        }
        #endregion
    }
}
