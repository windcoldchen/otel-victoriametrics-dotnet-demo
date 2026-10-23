using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OrderService.Diagnostics;

public static class AppMetrics
{
    public const string ServiceName = "OrderService";
    public const string ServiceVersion = "1.0.0";

    // ActivitySource for custom Traces
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    // Meter for custom Metrics
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // Custom Metrics
    public static readonly Counter<long> OrdersCreatedCounter = Meter.CreateCounter<long>(
        name: "orders.created",
        unit: "{orders}",
        description: "Total number of orders successfully created");

    public static readonly Histogram<double> OrderAmountHistogram = Meter.CreateHistogram<double>(
        name: "orders.amount",
        unit: "USD",
        description: "Monetary distribution of created orders");

    public static readonly UpDownCounter<long> ActiveProcessingOrders = Meter.CreateUpDownCounter<long>(
        name: "orders.active_processing",
        unit: "{orders}",
        description: "Number of orders currently being processed");

    public static readonly Counter<long> ValkeyCacheHitsCounter = Meter.CreateCounter<long>(
        name: "orders.valkey.cache_hits",
        unit: "{hits}",
        description: "Total number of Valkey cache hits");

    public static readonly Counter<long> ValkeyCacheMissesCounter = Meter.CreateCounter<long>(
        name: "orders.valkey.cache_misses",
        unit: "{misses}",
        description: "Total number of Valkey cache misses");

    public static readonly Counter<long> KafkaMessagesPublishedCounter = Meter.CreateCounter<long>(
        name: "orders.kafka.messages_published",
        unit: "{messages}",
        description: "Total number of order events published to Kafka");

    public static readonly Counter<long> OpenSearchDocumentsIndexedCounter = Meter.CreateCounter<long>(
        name: "orders.opensearch.documents_indexed",
        unit: "{documents}",
        description: "Total number of order documents indexed in OpenSearch");

    public static readonly Counter<long> PostgresOrdersInsertedCounter = Meter.CreateCounter<long>(
        name: "orders.postgres.rows_inserted",
        unit: "{rows}",
        description: "Total number of order rows inserted into PostgreSQL");
}
