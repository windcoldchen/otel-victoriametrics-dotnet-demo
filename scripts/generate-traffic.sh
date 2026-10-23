#!/usr/bin/env bash
set -e

# ==============================================================================
# 🚀 模擬測試流量產生腳本 (Simulated Traffic Generator for Observability)
# ==============================================================================
# 此腳本會向 OrderService 發送多種維度的請求：
# 1. POST /api/orders (正常交易：寫入 PG、Valkey、OpenSearch 並推送 Kafka)
# 2. GET /api/orders (快取查詢：檢驗 Valkey 9 命中與未命中)
# 3. GET /api/orders/search (全文檢索：檢驗 OpenSearch 3 搜尋查詢)
# 4. GET /api/orders/slow (慢查詢模擬：在 VictoriaTraces 中呈現長 Span 水瀑圖)
# 5. GET /api/orders/error (錯誤模擬：產生 500 異常、Error Span 與 Error 日誌)
# ==============================================================================

BASE_URL="http://localhost:8080/api/orders"
TOTAL_REQUESTS="${1:-30}"

echo "=================================================================="
echo " 🚦 開始發送多維度可觀測性測試流量 (目標: $BASE_URL)"
echo "    預計發送總請求批次: 約 $TOTAL_REQUESTS 筆"
echo "=================================================================="

PRODUCTS=("Mechanical Keyboard" "4K Gaming Monitor" "Ergonomic Chair" "Wireless Mouse" "USB-C Multi-port Dock" "Noise Canceling Headphones" "UltraWide OLED Display" "Smart Desk Mat")
CUSTOMERS=("CUST-1001" "CUST-2042" "CUST-8888" "CUST-9999" "VIP-001" "ENTERPRISE-77")

CREATED_ORDER_IDS=()

echo -e "\n[階段 1/5] 📦 發送正常訂單建立請求 (POST /api/orders)..."
for i in {1..12}; do
  RAND_PROD="${PRODUCTS[$RANDOM % ${#PRODUCTS[@]}]}"
  RAND_CUST="${CUSTOMERS[$RANDOM % ${#CUSTOMERS[@]}]}"
  RAND_AMT=$(awk -v min=29 -v max=1299 'BEGIN{srand(); printf "%.2f", min+rand()*(max-min)}')

  RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" -X POST "$BASE_URL" \
    -H "Content-Type: application/json" \
    -d "{\"customerId\":\"$RAND_CUST\",\"product\":\"$RAND_PROD\",\"amount\":$RAND_AMT}")

  HTTP_CODE=$(echo "$RESPONSE" | grep "HTTP_STATUS" | cut -d':' -f2)
  BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS")
  ORDER_ID=$(echo "$BODY" | grep -o '"id":"[^"]*' | cut -d'"' -f4 || echo "")

  if [ "$HTTP_CODE" -eq 201 ]; then
    echo "  ✅ [201 Created] OrderID: $ORDER_ID | 客戶: $RAND_CUST | 商品: $RAND_PROD | 金額: \$$RAND_AMT"
    if [ -n "$ORDER_ID" ]; then
      CREATED_ORDER_IDS+=("$ORDER_ID")
    fi
  else
    echo "  ❌ [Status: $HTTP_CODE] 請求失敗: $BODY"
  fi
  sleep 0.15
done

echo -e "\n[階段 2/5] ⚡ 查詢最新訂單列表與單筆查詢 (GET /api/orders)..."
for i in {1..6}; do
  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL")
  echo "  ✅ [Status: $HTTP_CODE] 查詢最新訂單 (觸發 Valkey 9 快取)"
  sleep 0.1
done

if [ ${#CREATED_ORDER_IDS[@]} -gt 0 ]; then
  for id in "${CREATED_ORDER_IDS[@]:0:3}"; do
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/$id")
    echo "  ✅ [Status: $HTTP_CODE] 依 ID 查詢訂單: $id"
    sleep 0.1
  done
fi

echo -e "\n[階段 3/5] 🔍 OpenSearch 3 全文檢索查詢 (GET /api/orders/search)..."
KEYWORDS=("Keyboard" "Monitor" "Chair" "Mouse" "CUST" "VIP")
for kw in "${KEYWORDS[@]}"; do
  COUNT=$(curl -s "$BASE_URL/search?q=$kw" | grep -o '"id":' | wc -l || echo "0")
  echo "  ✅ [OpenSearch] 搜尋關鍵字 '$kw' -> 檢索到 $COUNT 筆文件"
  sleep 0.1
done

echo -e "\n[階段 4/5] 🐢 觸發慢查詢模擬 (GET /api/orders/slow，觀察長 Span 瀑布圖)..."
for i in {1..3}; do
  RESULT=$(curl -s "$BASE_URL/slow")
  ELAPSED=$(echo "$RESULT" | grep -o '"elapsedMs":[0-9]*' | cut -d':' -f2 || echo "N/A")
  echo "  ⏳ [Slow Query] 耗時: ${ELAPSED}ms (產生 Trace 與 VictoriaTraces 長 Span)"
  sleep 0.2
done

echo -e "\n[階段 5/5] 💥 觸發模擬伺服器異常 (GET /api/orders/error，觀察 500 錯誤與日誌)..."
for i in {1..3}; do
  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/error" || true)
  echo "  ⚠️ [Status: $HTTP_CODE] 預期中的 500 異常 (產生 Error Span 與 Error Logs)"
  sleep 0.2
done

echo -e "\n=================================================================="
echo " 🎉 測試流量發送完成！"
echo " 📊 請立即開啟 Grafana (http://localhost:3000) 體驗 360 度觀測聯動："
echo "   1. Order Observability 360: http://localhost:3000/d/orders-360-observability"
echo "   2. Valkey 9 Dashboard:     http://localhost:3000/d/valkey-9-dashboard"
echo "   3. Apache Kafka Dashboard: http://localhost:3000/d/kafka-39-dashboard"
echo "   4. OpenSearch Dashboard:   http://localhost:3000/d/opensearch-3-dashboard"
echo "   5. PostgreSQL Dashboard:   http://localhost:3000/d/postgresql-17-dashboard"
echo "   6. VictoriaTraces (Jaeger): http://localhost:10428"
echo "   7. VictoriaLogs UI:        http://localhost:9428/select/vmui"
echo "=================================================================="
