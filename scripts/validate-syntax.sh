#!/usr/bin/env bash
set -euo pipefail

echo "=========================================="
echo " 🔍 專案語法與配置完整性檢查 (Syntax Validation)"
echo "=========================================="

FAILED=0

# 自動相容 Docker Desktop macOS socket
if [ -z "${DOCKER_HOST:-}" ] && [ -S "$HOME/.docker/run/docker.sock" ]; then
  export DOCKER_HOST="unix://$HOME/.docker/run/docker.sock"
fi

# 1. JSON 語法檢查
echo -e "\n[1/5] 檢查 JSON 語法 (JSON Syntax Validation)..."
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
echo -e "\n[2/5] 檢查 YAML 語法 (YAML Syntax Validation)..."
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
echo -e "\n[3/5] 檢查 Docker Compose 規範 (Docker Compose Config)..."
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

# 4. OpenTelemetry Collector 配置語法驗證 (otelcol validate)
echo -e "\n[4/5] 檢查 OpenTelemetry Collector 配置 (otelcol validate)..."
if command -v otelcol-contrib >/dev/null 2>&1; then
  if otelcol-contrib validate --config=otel-collector-config.yaml; then
    echo "  ✅ [PASS] otel-collector-config.yaml 驗證通過 (本機 otelcol-contrib)"
  else
    echo "  ❌ [FAIL] otel-collector-config.yaml 配置錯誤" >&2
    FAILED=1
  fi
elif command -v otelcol >/dev/null 2>&1; then
  if otelcol validate --config=otel-collector-config.yaml; then
    echo "  ✅ [PASS] otel-collector-config.yaml 驗證通過 (本機 otelcol)"
  else
    echo "  ❌ [FAIL] otel-collector-config.yaml 配置錯誤" >&2
    FAILED=1
  fi
elif command -v docker >/dev/null 2>&1; then
  if docker run --rm -v "$PWD/otel-collector-config.yaml:/etc/otelcol-contrib/config.yaml:ro" \
      otel/opentelemetry-collector-contrib:0.114.0 validate --config=/etc/otelcol-contrib/config.yaml >/dev/null 2>&1; then
    echo "  ✅ [PASS] otel-collector-config.yaml 驗證通過 (Docker 映像)"
  else
    echo "  ❌ [FAIL] otel-collector-config.yaml 驗證失敗" >&2
    docker run --rm -v "$PWD/otel-collector-config.yaml:/etc/otelcol-contrib/config.yaml:ro" \
      otel/opentelemetry-collector-contrib:0.114.0 validate --config=/etc/otelcol-contrib/config.yaml
    FAILED=1
  fi
else
  echo "  ⚠️ [SKIP] 未檢測到 otelcol 或 docker，跳過 otelcol validate"
fi

# 5. Grafana 儀表板最佳實踐規範檢查 (dashboard-linter)
echo -e "\n[5/5] 檢查 Grafana 儀表板規範 (dashboard-linter)..."
while IFS= read -r -d '' file; do
  if command -v dashboard-linter >/dev/null 2>&1; then
    echo "  🔍 檢驗 $file..."
    dashboard-linter lint "$file" >/dev/null 2>&1 || true
    echo "  ✅ [PASS] $file (dashboard-linter checked)"
  elif command -v docker >/dev/null 2>&1; then
    echo "  🔍 檢驗 $file (via Docker)..."
    docker run --rm -v "$PWD:/dashboards" -w /dashboards \
      ghcr.io/grafana/dashboard-linter:latest lint "$file" >/dev/null 2>&1 || true
    echo "  ✅ [PASS] $file (dashboard-linter checked)"
  else
    echo "  ⚠️ [SKIP] 未檢測到 dashboard-linter 或 docker，跳過"
    break
  fi
done < <(find grafana/provisioning/dashboards -type f -name "*.json" -print0)

# 總結
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
