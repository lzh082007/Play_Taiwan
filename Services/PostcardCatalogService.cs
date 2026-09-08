// 檔案路徑：System\Services\PostcardCatalogService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using backend.dao;
using backend.Models;
using backend.ViewModels;


namespace backend.Services
{
    /// <summary>明信片主檔 (md_postcard) 服務層。</summary>
    public class PostcardCatalogService
    {
        private readonly PostcardCatalogDao _dao;
        private readonly IConfiguration _configuration;


        public PostcardCatalogService(PostcardCatalogDao dao, IConfiguration configuration)
        {
            _dao = dao;
            _configuration = configuration;
        }


        #region 基本 CRUD 操作
        public async Task<List<PostcardCatalogResponse>> GetAllAsync(string category = null)
        {
            var entities = await _dao.GetAllAsync(category);
            return entities.Select(ToResponse).ToList();
        }


        public async Task<PostcardCatalogResponse> GetByIdAsync(string id)
        {
            var entity = await _dao.GetByIdAsync(id);
            return entity == null ? null : ToResponse(entity);
        }


        public async Task<List<PostcardCatalogResponse>> GetByStoryIdAsync(string storyId)
        {
            var entities = await _dao.GetByStoryIdAsync(storyId);
            return entities.Select(ToResponse).ToList();
        }


        public async Task<bool> DeleteAsync(string id)
        {
            return await _dao.DeleteAsync(id);
        }


        private static PostcardCatalogResponse ToResponse(Models.PostcardCatalog e) => new PostcardCatalogResponse
        {
            PostcardId = e.PostcardId,
            StoryId = e.StoryId,
            PostcardName = e.PostcardName,
            Summary = e.Summary,
            ImageUrl = e.ImageUrl,
            IsNightEditionDefault = e.IsNightEditionDefault,
            Category = e.Category,
            SortOrder = e.SortOrder,
            IsActive = e.IsActive,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        };
        #endregion


        #region AI 生成明信片
        /// <summary>
        /// 1. 呼叫外部 API 生成明信片
        /// 2. 直接把外部服務回傳的圖片網址 (download_url) 存入 MySQL 的 image_url 欄位
        ///    （不再下載圖片位元組、不再轉 Base64），前端後續可直接使用該網址顯示圖片
        /// </summary>
        public async Task<Models.PostcardCatalog> GenerateAiPostcardAsync(AiPostcardGenerateRequest request, string epId)
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();


            if (request.user_image != null)
            {
                var streamContent = new StreamContent(request.user_image.OpenReadStream());
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(request.user_image.ContentType);
                content.Add(streamContent, "user_image", request.user_image.FileName);
            }


            content.Add(new StringContent(request.spot_name ?? ""), "spot_name");
            content.Add(new StringContent(request.user_prompt ?? ""), "user_prompt");


            var apiUrl = "https://vlog.angelalala.com/api/postcard/create_ai";
            var response = await client.PostAsync(apiUrl, content);


            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"AI API 呼叫失敗，狀態碼: {response.StatusCode}");
            }


            var responseString = await response.Content.ReadAsStringAsync();
            var aiResult = JsonSerializer.Deserialize<AiPostcardApiResponse>(responseString);


            if (aiResult?.Status != "success" || string.IsNullOrEmpty(aiResult.DownloadUrl))
            {
                throw new Exception("外部 API 生成失敗或未回傳 download_url");
            }


            string unifiedPostcardId = "ai_" + Guid.NewGuid().ToString("N").Substring(0, 8);


            var newEntity = new Models.PostcardCatalog
            {
                PostcardId = unifiedPostcardId,
                StoryId = request.story_id ?? "Custom_AI",
                PostcardName = $"{request.spot_name} 專屬明信片",
                Summary = aiResult.PostcardIntroduction,
                ImageUrl = aiResult.DownloadUrl,
                IsNightEditionDefault = request.is_night_edition,
                Category = "AI Generate",
                SortOrder = 1,
                IsActive = true
            };


            await _dao.CreateAsync(newEntity);
            await _dao.BindPostcardToUserAsync(epId, newEntity.PostcardId);


            return newEntity;
        }
        #endregion


        #region 🌟 取出指定明信片的圖片網址 / 實體位元組 (供 Controller 顯示與 ibon 列印共用)
        /// <summary>
        /// 依 postcard_id 撈出該張明信片的圖片網址，供 Controller 直接轉址 (Redirect) 使用。
        /// </summary>
        public async Task<string> GetImageUrlByPostcardIdAsync(string postcardId)
        {
            var postcard = await _dao.GetByIdAsync(postcardId);
            return postcard?.ImageUrl;
        }


        /// <summary>
        /// 依 postcard_id 撈出該張明信片的圖片網址，並下載為實體位元組回傳。
        /// 僅供需要真正檔案內容的場景使用（例如 ibon 列印上傳）。
        /// </summary>
        public async Task<byte[]> GetImageBytesByPostcardIdAsync(string postcardId)
        {
            var postcard = await _dao.GetByIdAsync(postcardId);
            if (postcard == null || string.IsNullOrEmpty(postcard.ImageUrl)) return null;

            using var client = new HttpClient();
            return await client.GetByteArrayAsync(postcard.ImageUrl);
        }
        #endregion


        #region ibon 列印 (透過 Postcard_Id)
        /// <summary>
        /// 透過 postcard_id 找到指定的明信片，下載圖片網址內容並轉換為圖片發送給 ibon 微服務。
        /// </summary>
        public async Task<PostcardPrintResponse> PrintToIbonByPostcardIdAsync(string postcardId)
        {
            if (string.IsNullOrEmpty(postcardId))
                throw new Exception("請提供明信片 ID");


            // 統一呼叫共用的取圖方法，不再自己重複下載邏輯
            byte[] imageBytes = await GetImageBytesByPostcardIdAsync(postcardId);


            if (imageBytes == null)
                throw new Exception($"查無此明信片 (ID: {postcardId}) 的圖片內容，無法列印");


            using var httpClient = new HttpClient();
            using var form = new MultipartFormDataContent();


            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(fileContent, "file", "test_postcard.png");


            var ibonApiUrl = _configuration["IbonPrinterSettings:ApiUrl"] ?? "http://127.0.0.1:9000/upload";
            var response = await httpClient.PostAsync(ibonApiUrl, form);


            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"上傳至 ibon 失敗，狀態碼: {response.StatusCode}，錯誤內容: {errorMsg}");
            }


            var responseString = await response.Content.ReadAsStringAsync();


            string pinCode = "", deadline = "", qrCodeBase64 = "";
            using (var jsonDoc = JsonDocument.Parse(responseString))
            {
                var root = jsonDoc.RootElement;
                if (root.TryGetProperty("pincode", out var pin)) pinCode = pin.GetString();
                if (root.TryGetProperty("deadline", out var dl)) deadline = dl.GetString();
                if (root.TryGetProperty("qrcode_base64", out var qr)) qrCodeBase64 = qr.GetString();
            }


            return new PostcardPrintResponse
            {
                ibon_pickup_code = pinCode,
                pdf_url = "Base64 Image Data",
                deadline = deadline,
                qrcode_base64 = qrCodeBase64
            };
        }
        #endregion
    }
}