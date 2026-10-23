using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Diagnostics;

namespace OrderService.Services;

/// <summary>
/// Kafka 事件處理消費者：在背景消費 orders.events 主題的訂單建立訊息。
/// 支援 W3C TraceContext 解析，將生產端 (OrderService Producer) 與消費端 (Consumer) 串接為同一完整分散式鏈路。
/// </summary>
public class OrderProcessingConsumer : BackgroundService
{
    private readonly ILogger<OrderProcessingConsumer> _logger;
    private readonly IConfiguration _configuration;

    public OrderProcessingConsumer(ILogger<OrderProcessingConsumer> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafkaBootstrapServers = _configuration.GetConnectionString("Kafka")
            ?? _configuration["KAFKA_BOOTSTRAP_SERVERS"]
            ?? "localhost:9092";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaBootstrapServers,
            GroupId = "order-fulfillment-processor",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true,
            SessionTimeoutMs = 10000
        };

        // 啟動緩衝等待 Kafka 叢集穩定
        await Task.Delay(4000, stoppingToken);

        try
        {
            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe("orders.events");
            _logger.LogInformation("Kafka Consumer initialized and subscribed to 'orders.events'.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (consumeResult == null) continue;

                    // 1. 從 Kafka Message Headers 提取 W3C traceparent
                    string? traceparent = null;
                    if (consumeResult.Message.Headers != null && consumeResult.Message.Headers.TryGetLastBytes("traceparent", out var headerBytes))
                    {
                        traceparent = Encoding.UTF8.GetString(headerBytes);
                    }

                    ActivityContext parentContext = default;
                    if (!string.IsNullOrEmpty(traceparent))
                    {
                        ActivityContext.TryParse(traceparent, null, out parentContext);
                    }

                    // 2. 建立消費端 Activity Span (繼承生產端 TraceId)
                    using var activity = AppMetrics.ActivitySource.StartActivity(
                        "KafkaConsume:orders.events",
                        ActivityKind.Consumer,
                        parentContext);

                    activity?.SetTag("messaging.system", "kafka");
                    activity?.SetTag("messaging.destination.name", "orders.events");
                    activity?.SetTag("messaging.kafka.message.key", consumeResult.Message.Key);
                    activity?.SetTag("messaging.kafka.partition", consumeResult.Partition.Value);
                    activity?.SetTag("messaging.kafka.offset", consumeResult.Offset.Value);

                    var currentTraceId = activity?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString() ?? "00000000000000000000000000000000";

                    _logger.LogInformation(
                        "Kafka Consumer successfully processed order event for {OrderId}. Partition: {Partition}, Offset: {Offset}. TraceId: {TraceId}",
                        consumeResult.Message.Key, consumeResult.Partition.Value, consumeResult.Offset.Value, currentTraceId);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning("Kafka consume warning: {Reason}", ex.Error.Reason);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Kafka message in consumer loop.");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka consumer background task exited.");
        }
    }
}
