## 📌 變更類型 (PR Type)
請選擇本次變更的主要性質：
- [ ] 🚀 新功能 (Feature)
- [ ] 🐛 問題修復 (Bug Fix)
- [ ] ⚡ 效能優化 (Performance Improvement)
- [ ] ♻️ 程式碼重構 (Refactoring)
- [ ] 📝 文檔更新 (Documentation)
- [ ] 🛡️ CI/CD 與建構流程 (CI/CD / Build)
- [ ] 🧪 測試案例 (Unit/Integration Test)

---

## 📝 變更描述 (Description)
請簡明扼要說明本次 PR 的核心改動背景與實作內容：

---

## 🔗 關聯 Issue (Related Issues)
- 關聯需求或 Bug: Closes #

---

## 🧪 自行驗證清單 (Verification Checklist)
在送出 PR 前，請確認已完成以下各項驗證：
- [ ] 已於本地成功執行 `./scripts/run-ci-local.sh` 且所有檢查皆通過 (PASS)
- [ ] 已通過 `./scripts/validate-syntax.sh`（YAML / JSON / Compose 語法無誤）
- [ ] C# 程式碼格式符合標準 (`dotnet format --verify-no-changes`)
- [ ] 方案編譯無任何警告與錯誤 (`dotnet build --warnaserror`)
- [ ] 所有單元測試皆正常通過 (`dotnet test`)
- [ ] Docker 多階段構建成功且可正常運行容器

---

## ⚠️ 破壞性變更 (Breaking Changes)
- [ ] 本 PR 包含 Breaking Changes（若有，請在此詳述相容性遷移方案）
- [ ] 無破壞性變更
