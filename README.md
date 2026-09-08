🇹🇼 Play Taiwan
結合地圖探索、任務系統、明信片收集與 AI 生成 Vlog 的智慧旅遊遊戲化後端







📖 專案簡介
Play Taiwan 是一套以 ASP.NET Core 打造的智慧旅遊遊戲化系統後端。玩家可以在地圖上探索景點與店家、完成系統派發的任務、拍照驗證任務成果、收集城市明信片、累積徽章，並透過 AI 服務自動生成旅程 Vlog，讓「玩台灣」變成一場可累積、可分享的收集型冒險。

專案採用 Controller / Service / DAO 分層架構，資料儲存以 MySQL 為主、Neo4j 圖形資料庫處理地點與路線關聯，並以 JWT 驗證保護 API。

✨ 核心功能
模組	說明
🔐 帳號驗證	JWT 登入 / 註冊，Middleware 攔截驗證 Token
🗺️ 地圖探索	景點、店家（Merchant）地圖資訊查詢
🎯 任務系統	任務生成（Task Generation）、任務提示（Hint）、任務成果驗證（Verification）
🏅 徽章成就	依任務完成度發放 Badge
📮 明信片收藏	明信片圖鑑（Catalog）與個人收集紀錄
🖼️ 剪影猜景點	以景點剪影作為互動猜謎小遊戲
📜 故事劇情	Story 模組提供地區文化 / 歷史敘事內容
🎬 AI Vlog 生成	透過 AI 服務將旅程紀錄自動剪輯成 Vlog
🕒 歷史紀錄	使用者操作 / 任務歷程查詢
📤 檔案上傳	任務驗證照片、明信片素材等檔案上傳
✉️ 通知信件	Email 服務發送系統通知
🏗️ 技術架構
text
Client (App / Web)
      │  REST API (JWT Bearer)
      ▼
┌─────────────────────────────┐
│   Controllers                │  ← 接收請求、參數驗證
├─────────────────────────────┤
│   Services                   │  ← 商業邏輯（任務生成/驗證、AI Vlog、Neo4j 查詢…）
├─────────────────────────────┤
│   DAO                        │  ← 資料存取邏輯
├─────────────────────────────┤
│   MySQL          Neo4j       │  ← 關聯式資料      圖形資料（景點/路線關聯）
└─────────────────────────────┘
🧰 技術棧
後端框架：ASP.NET Core（C#）

資料庫：MySQL、Neo4j

驗證機制：JWT + Middleware

API 文件：Swagger

其他整合：Email 通知服務、AI Vlog 生成服務

📁 專案結構
text
Play_Taiwan/
├── Controllers/        # API 進入點（Auth, Map, Task, Postcard, Badge, Story, Silhouette…）
├── Services/            # 商業邏輯層（含 AI Vlog、Neo4j、任務生成/驗證服務）
├── dao/                 # 資料存取層
├── Models/              # 資料模型
├── ViewModels/          # 前後端傳輸用視圖模型
├── Middleware/           # JWT 驗證等中介層
├── Extensions/           # 擴充方法
├── util/                 # 共用工具
├── Sqls/mysql/           # MySQL 建表 / 初始化腳本
├── wwwroot/               # 靜態資源
├── image/                 # 圖片素材
├── appsettings.json                       # 主要設定檔
├── appsettings.Development.json.example    # 開發環境設定範例
└── Program.cs / Startup.cs                 # 應用程式進入點與服務註冊
🚀 快速開始
環境需求
.NET SDK（版本請參考 global.json）

MySQL 資料庫

Neo4j 資料庫

安裝與設定
複製專案

bash
git clone https://github.com/pojkhb/Play_Taiwan.git
cd Play_Taiwan
設定環境變數 複製設定檔範例並填入自己的資料庫連線字串、JWT 密鑰等機敏資訊：

bash
cp appsettings.Development.json.example appsettings.Development.json
再依實際環境編輯 appsettings.Development.json 中的 MySQL / Neo4j 連線字串、JWT 設定與其他金鑰。

建立資料庫 使用 Sqls/mysql/ 內的腳本建立所需資料表。

還原套件並啟動專案

bash
dotnet restore
dotnet run
開啟 Swagger 文件 專案啟動後於瀏覽器開啟：

text
https://localhost:<port>/swagger
即可查看並測試所有 API。

📡 API 總覽
Controller	用途
Controller	用途
AuthController	註冊 / 登入 / JWT 發行
MapController	地圖與地點查詢
MerchantController	店家資訊
TaskController / TaskHintController	任務派發、提示
PostcardController / PostcardCatalogController	明信片收集與圖鑑
BadgeController	徽章成就
StoryController	劇情敘事內容
SilhouetteController	剪影猜景點小遊戲
HistoryController	使用者歷程紀錄
UploadController	檔案上傳
詳細請求參數與回應格式請以 Swagger UI 為準。

🔒 安全性注意事項
appsettings.json 中請勿提交任何真實資料庫密碼或 JWT 密鑰，敏感設定請放在 appsettings.Development.json（已加入 .gitignore）。

API 皆透過 JWT Middleware 驗證身份，請在請求標頭中帶入 Authorization: Bearer <token>。

🤝 貢獻方式
歡迎提出 Issue 或 Pull Request：

Fork 本專案

建立功能分支（git checkout -b feature/your-feature）

提交變更（git commit -m "feat: 說明你的變更"）

推送分支並發送 Pull Request

📄 授權
<!-- TODO: 請補充授權條款，例如 MIT License -->

👤 作者
GitHub: @pojkhb