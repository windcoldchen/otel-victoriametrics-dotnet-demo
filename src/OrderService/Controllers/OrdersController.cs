using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using OpenSearch.Client;
using OrderService.Diagnostics;
using OrderService.Models;
using StackExchange.Redis;
using Order = OrderService.Models.Order;
using Headers = Confluent.Kafka.Headers;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly NpgsqlDataSource _npgsqlDataSource;
    private readonly IConnectionMultiplexer _valkey;
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly IOpenSearchClient _openSearchClient;
    private readonly ILogger<OrdersController> _logger;
    private readonly HttpClient _httpClient;

    public OrdersController(
        NpgsqlDataSource npgsqlDataSource,
        IConnectionMultiplexer valkey,
        IProducer<string, string> kafkaProducer,
        IOpenSearchClient openSearchClient,
        ILogger<OrdersController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _npgsqlDataSource = npgsqlDataSource;
        _valkey = valkey;
        _kafkaProducer = kafkaProducer;
        _openSearchClient = openSearchClient;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("ExternalService");
    }

    /// <summary>
    /// 取得所有訂單 (優先讀取 Valkey 9 快取，未命中時回退至 PostgreSQL)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";

        // 1. 嘗試從 Valkey 9 快取讀取
        try
        {
            var db = _valkey.GetDatabase();
            var cachedOrders = await db.ListRangeAsync("orders:recent", 0, 49);
            if (cachedOrders.Length > 0)
            {
                AppMetrics.ValkeyCacheHitsCounter.Add(1);
                var list = cachedOrders
                    .Select(c => JsonSerializer.Deserialize<Order>(c.ToString()!))
                    .Where(o => o != null)
                    .ToList();

                _logger.LogInformation("Retrieved {Count} orders from Valkey 9 cache. TraceId: {TraceId}", list.Count, traceId);
                return Ok(list);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read orders from Valkey 9 cache, falling back to PostgreSQL. TraceId: {TraceId}", traceId);
        }

        AppMetrics.ValkeyCacheMissesCounter.Add(1);

        // 2. 從 PostgreSQL 查詢
        var orders = new List<Order>();
        try
        {
            await using var conn = await _npgsqlDataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, customer_id, product, amount, status, created_at_utc FROM orders ORDER BY created_at_utc DESC LIMIT 50", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                orders.Add(new Order(
                    Id: reader.GetString(0),
                    CustomerId: reader.GetString(1),
                    Product: reader.GetString(2),
                    Amount: Convert.ToDouble(reader.GetDecimal(3)),
                    Status: reader.GetString(4),
                    CreatedAtUtc: reader.GetDateTime(5)
                ));
            }

            _logger.LogInformation("Retrieved {Count} orders directly from PostgreSQL database. TraceId: {TraceId}", orders.Count, traceId);

            // 回填 Valkey 快取
            if (orders.Count > 0 && _valkey.IsConnected)
            {
                try
                {
                    var db = _valkey.GetDatabase();
                    var batch = orders.Select(o => (RedisValue)JsonSerializer.Serialize(o)).ToArray();
                    await db.KeyDeleteAsync("orders:recent");
                    await db.ListRightPushAsync("orders:recent", batch);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to backfill Valkey 9 cache. TraceId: {TraceId}", traceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query orders from PostgreSQL. TraceId: {TraceId}", traceId);
            return StatusCode(500, new { error = "Database query failure", detail = ex.Message });
        }

        return Ok(orders);
    }

    /// <summary>
    /// 依 ID 取得訂單 (Valkey 9 快取 ➔ PostgreSQL 資料庫)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrderById(string id)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";

        // 1. Valkey 9 快取
        try
        {
            var db = _valkey.GetDatabase();
            var cachedJson = await db.StringGetAsync($"order:{id}");
            if (cachedJson.HasValue)
            {
                AppMetrics.ValkeyCacheHitsCounter.Add(1);
                var cachedOrder = JsonSerializer.Deserialize<Order>(cachedJson.ToString()!);
                if (cachedOrder != null)
                {
                    _logger.LogInformation("Retrieved order {OrderId} from Valkey 9 cache. TraceId: {TraceId}", id, traceId);
                    return Ok(cachedOrder);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Valkey query failed for order {OrderId}. TraceId: {TraceId}", id, traceId);
        }

        AppMetrics.ValkeyCacheMissesCounter.Add(1);

        // 2. PostgreSQL
        try
        {
            await using var conn = await _npgsqlDataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, customer_id, product, amount, status, created_at_utc FROM orders WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var order = new Order(
                    Id: reader.GetString(0),
                    CustomerId: reader.GetString(1),
                    Product: reader.GetString(2),
                    Amount: Convert.ToDouble(reader.GetDecimal(3)),
                    Status: reader.GetString(4),
                    CreatedAtUtc: reader.GetDateTime(5)
                );

                // 異步回填快取
                try
                {
                    var db = _valkey.GetDatabase();
                    await db.StringSetAsync($"order:{id}", JsonSerializer.Serialize(order), TimeSpan.FromMinutes(30));
                }
                catch { }

                _logger.LogInformation("Retrieved order {OrderId} from PostgreSQL database. TraceId: {TraceId}", id, traceId);
                return Ok(order);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch order {OrderId} from PostgreSQL. TraceId: {TraceId}", id, traceId);
            return StatusCode(500, new { error = "Database query error", detail = ex.Message });
        }

        _logger.LogWarning("Order {OrderId} not found. TraceId: {TraceId}", id, traceId);
        return NotFound(new { message = $"Order {id} not found" });
    }

    /// <summary>
    /// 全文檢索訂單 (使用 OpenSearch 3 分散式搜尋引擎)
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<Order>>> SearchOrders([FromQuery] string q)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";

        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Query parameter 'q' is required" });
        }

        using var activity = AppMetrics.ActivitySource.StartActivity("OpenSearch:SearchOrders");
        activity?.SetTag("db.system", "opensearch");
        activity?.SetTag("search.query", q);

        try
        {
            _logger.LogInformation("Searching orders in OpenSearch 3 with keyword: {Query}. TraceId: {TraceId}", q, traceId);
            var searchResponse = await _openSearchClient.SearchAsync<Order>(s => s
                .Index("orders")
                .Query(query => query
                    .MultiMatch(m => m
                        .Fields(f => f.Field(o => o.Product).Field(o => o.CustomerId))
                        .Query(q)
                    )
                )
            );

            if (!searchResponse.IsValid)
            {
                _logger.LogWarning("OpenSearch query error: {Reason}. TraceId: {TraceId}", searchResponse.ServerError?.Error?.Reason ?? searchResponse.DebugInformation, traceId);
            }

            return Ok(searchResponse.Documents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute search against OpenSearch 3. TraceId: {TraceId}", traceId);
            return StatusCode(500, new { error = "Search engine error", detail = ex.Message });
        }
    }

    /// <summary>
    /// 建立訂單 (依序寫入 PostgreSQL、Valkey 9、OpenSearch 3，並發布事件至 Apache Kafka 3.9)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var orderId = $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        AppMetrics.ActiveProcessingOrders.Add(1);

        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";

        try
        {
            _logger.LogInformation("Creating new order {OrderId} for customer {CustomerId} with amount {Amount:C}. TraceId: {TraceId}",
                orderId, request.CustomerId, request.Amount, traceId);

            // 1. 自定義 Span: 驗證訂單庫存
            using (var inventoryActivity = AppMetrics.ActivitySource.StartActivity("CheckInventory"))
            {
                inventoryActivity?.SetTag("order.id", orderId);
                inventoryActivity?.SetTag("inventory.product", request.Product);
                await Task.Delay(Random.Shared.Next(10, 30));
                _logger.LogInformation("Inventory check passed for product {Product}. TraceId: {TraceId}", request.Product, traceId);
            }

            // 2. 自定義 Span: 處理金流與下游 HTTP 調用
            using (var paymentActivity = AppMetrics.ActivitySource.StartActivity("ProcessPayment"))
            {
                paymentActivity?.SetTag("order.id", orderId);
                paymentActivity?.SetTag("payment.amount", request.Amount);
                paymentActivity?.SetTag("payment.provider", "MockBankGateway");

                await Task.Delay(Random.Shared.Next(20, 60));

                try
                {
                    _ = await _httpClient.GetAsync("https://httpbin.org/get");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Downstream mock gateway call timed out or failed (non-critical). TraceId: {TraceId}", traceId);
                }

                _logger.LogInformation("Payment of {Amount:C} authorized successfully for order {OrderId}. TraceId: {TraceId}",
                    request.Amount, orderId, traceId);
            }

            var order = new Order(
                Id: orderId,
                CustomerId: request.CustomerId,
                Product: request.Product,
                Amount: request.Amount,
                Status: "Completed",
                CreatedAtUtc: DateTime.UtcNow
            );

            // 3. PostgreSQL: 寫入關聯式交易資料庫 (自動產生 Npgsql OTel Span)
            try
            {
                await using var conn = await _npgsqlDataSource.OpenConnectionAsync();
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO orders (id, customer_id, product, amount, status, created_at_utc)
                    VALUES (@id, @customerId, @product, @amount, @status, @createdAtUtc)
                    """, conn);

                cmd.Parameters.AddWithValue("id", order.Id);
                cmd.Parameters.AddWithValue("customerId", order.CustomerId);
                cmd.Parameters.AddWithValue("product", order.Product);
                cmd.Parameters.AddWithValue("amount", (decimal)order.Amount);
                cmd.Parameters.AddWithValue("status", order.Status);
                cmd.Parameters.AddWithValue("createdAtUtc", order.CreatedAtUtc);

                await cmd.ExecuteNonQueryAsync();
                AppMetrics.PostgresOrdersInsertedCounter.Add(1);
                _logger.LogInformation("Order {OrderId} saved to PostgreSQL. TraceId: {TraceId}", orderId, traceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist order {OrderId} into PostgreSQL. TraceId: {TraceId}", orderId, traceId);
                throw;
            }

            // 4. Valkey 9: 寫入快取與最近訂單佇列 (自動產生 StackExchangeRedis OTel Span)
            try
            {
                var db = _valkey.GetDatabase();
                var orderJson = JsonSerializer.Serialize(order);
                await db.StringSetAsync($"order:{order.Id}", orderJson, TimeSpan.FromMinutes(30));
                await db.ListLeftPushAsync("orders:recent", orderJson);
                await db.ListTrimAsync("orders:recent", 0, 49);
                _logger.LogInformation("Order {OrderId} cached in Valkey 9. TraceId: {TraceId}", orderId, traceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache order {OrderId} in Valkey 9 (non-critical). TraceId: {TraceId}", orderId, traceId);
            }

            // 5. OpenSearch 3: 寫入全文檢索索引 (由 HttpClientInstrumentation 追蹤 HTTP 呼叫)
            try
            {
                var indexResponse = await _openSearchClient.IndexDocumentAsync(order);
                if (indexResponse.IsValid)
                {
                    AppMetrics.OpenSearchDocumentsIndexedCounter.Add(1);
                    _logger.LogInformation("Order {OrderId} indexed into OpenSearch 3. TraceId: {TraceId}", orderId, traceId);
                }
                else
                {
                    _logger.LogWarning("OpenSearch indexing issue: {Reason}. TraceId: {TraceId}", indexResponse.ServerError?.Error?.Reason ?? indexResponse.DebugInformation, traceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index order {OrderId} into OpenSearch 3 (non-critical). TraceId: {TraceId}", orderId, traceId);
            }

            // 6. Apache Kafka 3.9: 發布事件訊息 (自定義 Producer Span 並注入 W3C TraceContext)
            try
            {
                using var kafkaActivity = AppMetrics.ActivitySource.StartActivity("KafkaProduce:orders.events", ActivityKind.Producer);
                kafkaActivity?.SetTag("messaging.system", "kafka");
                kafkaActivity?.SetTag("messaging.destination.name", "orders.events");
                kafkaActivity?.SetTag("messaging.kafka.message.key", order.Id);

                var kafkaMessage = new Message<string, string>
                {
                    Key = order.Id,
                    Value = JsonSerializer.Serialize(order),
                    Headers = new Headers()
                };

                // 將當前 TraceContext 注入 Kafka Headers 實現跨進程追蹤
                if (kafkaActivity != null)
                {
                    kafkaMessage.Headers.Add("traceparent", Encoding.UTF8.GetBytes(kafkaActivity.Id ?? string.Empty));
                }

                await _kafkaProducer.ProduceAsync("orders.events", kafkaMessage);
                AppMetrics.KafkaMessagesPublishedCounter.Add(1);
                _logger.LogInformation("Order {OrderId} event published to Apache Kafka 3.9 topic 'orders.events'. TraceId: {TraceId}", orderId, traceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish order {OrderId} event to Kafka (non-critical). TraceId: {TraceId}", orderId, traceId);
            }

            // 7. 記錄業務指標
            AppMetrics.OrdersCreatedCounter.Add(1, new KeyValuePair<string, object?>("product", request.Product));
            AppMetrics.OrderAmountHistogram.Record(request.Amount, new KeyValuePair<string, object?>("customer.tier", "standard"));

            _logger.LogInformation("Order {OrderId} completely fulfilled across all datastores! TraceId: {TraceId}",
                orderId, traceId);

            return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, order);
        }
        finally
        {
            AppMetrics.ActiveProcessingOrders.Add(-1);
        }
    }

    /// <summary>
    /// 模擬慢查詢 (用以觀察 VictoriaTraces 水瀑圖長 Span)
    /// </summary>
    [HttpGet("slow")]
    public async Task<IActionResult> SimulateSlowProcessing()
    {
        using var activity = AppMetrics.ActivitySource.StartActivity("SlowDatabaseQuery");
        var delayMs = Random.Shared.Next(800, 1500);
        activity?.SetTag("db.query.table", "OrdersArchive");
        activity?.SetTag("db.delay_ms", delayMs);

        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";
        _logger.LogWarning("Executing heavy historical query... Expected duration: {Duration}ms. TraceId: {TraceId}", delayMs, traceId);
        await Task.Delay(delayMs);

        return Ok(new { message = "Heavy computation completed", elapsedMs = delayMs, traceId });
    }

    /// <summary>
    /// 模擬未處理例外 (用以觀察 500 指標、Error Span 與 Error 日誌)
    /// </summary>
    [HttpGet("error")]
    public IActionResult SimulateError()
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";
        _logger.LogError("Critical payment processing error encountered! Database connection pool exhausted! TraceId: {TraceId}", traceId);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "Database connection pool exhausted");

        throw new InvalidOperationException($"Simulated 500 Server Error: Connection pool exhausted while processing payment transaction. TraceId: {traceId}");
    }
}

