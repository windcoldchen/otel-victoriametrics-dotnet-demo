# 🤝 軟體開發標準協作指引 (Contributing Guide)

感謝您參與本專案的開發與維護！為了保障代碼品質、可維護性與系統穩定性，所有參與者請遵循以下標準開發規範。

---

## 🌿 1. 分支管理策略 (Branching Strategy)

- **`main` 分支**：受保護主幹分支，禁止直接 Push 或 Force Push。所有變更均必須經由 Pull Request 通過 CI 驗證後合併。
- **功能分支 (Feature Branches)**：從最新 `main` 建立功能分支，命名規範如下：
  - `feat/<簡要功能描述>`：新增業務功能或監控看板（例如：`feat/kafka-producer-metrics`）
  - `fix/<問題描述>`：Bug 或配置修復（例如：`fix/opensearch-index-mapping`）
  - `refactor/<重構目標>`：架構改進或代碼簡化（例如：`refactor/otlp-pipeline-setup`）
  - `ci/<流水線調整>`：CI/CD 與自動化腳本更新（例如：`ci/trivy-security-scan`）

---

## 💬 2. Git Commit 提交規範 (Conventional Commits)

Commit 訊息請遵循 [Conventional Commits](https://www.conventionalcommits.org/) 標準：

```text
<類型>(<範疇>): <簡潔描述動詞開頭>

[可選詳細內文說明背景與原因]
```

常見類型：
- `feat`：新增功能或儀表板
- `fix`：修復 Bug
- `docs`：說明文件更新
- `style`：不影響程式邏輯的程式碼格式調整
- `refactor`：既非新功能亦非修復的重構
- `perf`：效能調優
- `test`：新增或維護測試用例
- `ci`：CI/CD 流程、Docker 構建或工具鏈調整

---

## 🛡️ 3. 本地開發與品質門禁驗證

在推送分支或發起 PR 前，請務必於本機執行驗證腳本：

```bash
# 完整 CI 流水線本地驗證（語法校驗 + C# 風格 + 單元測試 + Docker 構建）
./scripts/run-ci-local.sh

# 僅快速檢查 YAML / JSON / Docker Compose 語法
./scripts/validate-syntax.sh
```

---

## 🚀 4. Pull Request 合併門禁標準

所有發起至 `main` 的 PR 必須滿足以下全部條件方可合併：
1. **CI 狀態全部通過**：GitHub Actions 五大階段必須呈現綠燈（PASS）。
2. **與主幹保持同步**：PR 分支必須為最新 `main` 之延伸（Linear History）。
3. **對話全數解決**：所有 Code Review 意見與討論均需標記為 Resolved。
4. **規範標題與內文**：使用 PR 範本清楚勾選驗證項目。
