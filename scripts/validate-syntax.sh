#!/usr/bin/env bash
set -euo pipefail

echo "=========================================="
echo " 🔍 專案語法與配置完整性檢查 (Syntax Validation)"
echo "=========================================="

FAILED=0

# 1. JSON 語法檢查
echo -e "\n[1/4] 檢查 JSON 語法 (JSON Syntax Validation)..."
while IFS= read -r -d '' file; do
  if command -v jq >/dev/null 2>&1; then
    if jq empty "$file" >/dev/null 2>&1; then
      echo "  ✅ [PASS] $file"
    else
      echo "  ❌ [FAIL] $file: JSON 語法錯誤" >&2
      jq empty "$file" >&2
      FAILED=1
    fi
  else
    if ruby -rjson -e 'JSON.parse(File.read(ARGV[0]))' "$file" >/dev/null 2>&1; then
      echo "  ✅ [PASS] $file"
    else
      echo "  ❌ [FAIL] $file: JSON 語法錯誤" >&2
      ruby -rjson -e 'JSON.parse(File.read(ARGV[0]))' "$file" >&2
      FAILED=1
    fi
  fi
done < <(find . -type f -name "*.json" ! -path "*/bin/*" ! -path "*/obj/*" ! -path "*/.git/*" -print0)

# 2. YAML 語法檢查
echo -e "\n[2/4] 檢查 YAML 語法 (YAML Syntax Validation)..."
while IFS= read -r -d '' file; do
  if ruby -ryaml -e 'YAML.load_file(ARGV[0])' "$file" >/dev/null 2>&1; then
    echo "  ✅ [PASS] $file"
  else
    echo "  ❌ [FAIL] $file: YAML 語法解析錯誤" >&2
    ruby -ryaml -e 'YAML.load_file(ARGV[0])' "$file" >&2
    FAILED=1
  fi
done < <(find . -type f \( -name "*.yaml" -o -name "*.yml" \) ! -path "*/.git/*" -print0)

# 3. Docker Compose 語法與結構檢查
echo -e "\n[3/4] 檢查 Docker Compose 規範 (Docker Compose Config)..."
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  if docker compose config --quiet >/dev/null 2>&1; then
    echo "  ✅ [PASS] docker-compose.yml 結構與語法正確"
  else
    echo "  ❌ [FAIL] docker-compose.yml 語法驗證失敗" >&2
    docker compose config
    FAILED=1
  fi
else
  echo "  ⚠️ [SKIP] 系統未安裝 docker compose，跳過 Docker Compose 檢查"
fi

# 4. 總結
echo -e "\n=========================================="
if [ "$FAILED" -eq 0 ]; then
  echo " 🎉 所有語法檢查均通過 (All syntax checks passed!)"
  echo "=========================================="
  exit 0
else
  echo " ❌ 發現語法錯誤，請檢查上方錯誤資訊 (Syntax check failed!)"
  echo "=========================================="
  exit 1
fi
