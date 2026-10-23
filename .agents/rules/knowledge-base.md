# 專案知識庫整合規範 (Centralized Knowledge Base Policy)

## 核心原則
- **唯一中央知識庫**：`windcold-kb`（GitHub 儲存庫: `windcoldchen/windcold-kb`，本地路徑：`/Users/luke.chen/Documents/github/windcold-kb`）為所有專案架構、技術文件與運維指南的中央知識庫。
- **文檔同步要求**：本專案（`otel-victoriametrics-dotnet-demo`）及任何其他專案中，凡有架構設計、可觀測性指南、中介軟體整合、CI/CD 流程或技術文檔之新增或修改，**均必須同步或發布對應文檔至 `windcold-kb` 儲存庫**。

---

## 1. 文檔撰寫與組織標準
1. 專案文檔統一放置於 `windcold-kb/docs/notes/`。
2. 每次新增或更新文檔後，必須更新 `windcold-kb/mkdocs.yml` 中的 `nav:` 導覽配置。
3. 遵循 Material for MkDocs 語法擴展（含 Admonitions、Tabbed、Mermaid 雙引號標準）。

---

## 2. windcold-kb 驗證標準
在 `windcold-kb` 目錄中執行：
1. **Markdown 格式校驗**：
   ```bash
   npm run lint:md
   ```
2. **MkDocs 嚴格編譯**：
   ```bash
   source .venv/bin/activate
   DISABLE_MKDOCS_2_WARNING="true" MKDOCS_GIT_COMMITTERS_APIKEY=$(gh auth token) mkdocs build --strict
   ```

---

## 3. 自動化與發布工作流
1. 在 `windcold-kb` 開立特徵分支（如 `docs/otel-victoriametrics-dotnet-demo`）。
2. Commit 訊息遵循 Conventional Commits（如 `docs: 新增 .NET 10 OTel + VictoriaMetrics 實戰專案文檔`）。
3. 透過 `gh pr create` 開立 PR，驗證無誤後以 Squash & Merge 方式合併回 `main` 分支。
