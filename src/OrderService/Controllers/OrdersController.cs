using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderService.Diagnostics;
using OrderService.Models;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private static readonly List<Order> Orders = new();
    private readonly ILogger<OrdersController> _logger;
    private readonly HttpClient _httpClient;

    public OrdersController(ILogger<OrdersController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("ExternalService");
    }

    /// <summary>
    /// 取得所有訂單
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<Order>> GetOrders()
    {
        _logger.LogInformation("Retrieving all orders. Total count: {Count}", Orders.Count);
        return Ok(Orders);
    }

    /// <summary>
    /// 建立訂單 (觸發 Metrics、Logs 與多層 Span 追蹤)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Order>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var orderId = $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        AppMetrics.ActiveProcessingOrders.Add(1);

        try
        {
            _logger.LogInformation("Creating new order {OrderId} for customer {CustomerId} with amount {Amount:C}",
                orderId, request.CustomerId, request.Amount);

            // 1. 自定義 Span: 驗證訂單庫存
            using (var inventoryActivity = AppMetrics.ActivitySource.StartActivity("CheckInventory"))
            {
                inventoryActivity?.SetTag("order.id", orderId);
                inventoryActivity?.SetTag("inventory.product", request.Product);
                await Task.Delay(Random.Shared.Next(10, 40)); // 模擬庫存檢查延遲
                _logger.LogInformation("Inventory check passed for product {Product}", request.Product);
            }

            // 2. 自定義 Span: 處理支付扣款
            using (var paymentActivity = AppMetrics.ActivitySource.StartActivity("ProcessPayment"))
            {
                paymentActivity?.SetTag("order.id", orderId);
                paymentActivity?.SetTag("payment.amount", request.Amount);
                paymentActivity?.SetTag("payment.provider", "MockBankGateway");

                await Task.Delay(Random.Shared.Next(30, 80)); // 模擬金流調用

                // 模擬下游 HTTP 調用，展示 W3C TraceContext 跨進程傳播
                try
                {
                    _ = await _httpClient.GetAsync("https://httpbin.org/get");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Downstream mock gateway call timed out or failed (non-critical)");
                }

                _logger.LogInformation("Payment of {Amount:C} authorized successfully for order {OrderId}",
                    request.Amount, orderId);
            }

            var order = new Order(
                Id: orderId,
                CustomerId: request.CustomerId,
                Product: request.Product,
                Amount: request.Amount,
                Status: "Completed",
                CreatedAtUtc: DateTime.UtcNow
            );

            Orders.Add(order);

            // 3. 記錄自定義業務指標
            AppMetrics.OrdersCreatedCounter.Add(1, new KeyValuePair<string, object?>("product", request.Product));
            AppMetrics.OrderAmountHistogram.Record(request.Amount, new KeyValuePair<string, object?>("customer.tier", "standard"));

            _logger.LogInformation("Order {OrderId} fulfilled successfully! TraceId: {TraceId}",
                orderId, Activity.Current?.TraceId.ToString());

            return CreatedAtAction(nameof(GetOrders), new { id = order.Id }, order);
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

        _logger.LogWarning("Executing heavy historical query... Expected duration: {Duration}ms", delayMs);
        await Task.Delay(delayMs);

        return Ok(new { message = "Heavy computation completed", elapsedMs = delayMs });
    }

    /// <summary>
    /// 模擬未處理例外 (用以觀察 500 指標、Error Span 與 Error 日誌)
    /// </summary>
    [HttpGet("error")]
    public IActionResult SimulateError()
    {
        _logger.LogError("Critical payment processing error encountered! Database connection pool exhausted!");
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "Database connection pool exhausted");

        throw new InvalidOperationException("Simulated 500 Server Error: Connection pool exhausted while processing payment transaction.");
    }
}
