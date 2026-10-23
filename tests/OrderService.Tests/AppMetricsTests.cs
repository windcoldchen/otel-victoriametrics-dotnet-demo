using OrderService.Diagnostics;
using Xunit;

namespace OrderService.Tests;

public class AppMetricsTests
{
    [Fact]
    public void AppMetrics_ShouldDefineExpectedServiceConstants()
    {
        Assert.Equal("OrderService", AppMetrics.ServiceName);
        Assert.Equal("1.0.0", AppMetrics.ServiceVersion);
    }

    [Fact]
    public void AppMetrics_ActivitySourceAndMeter_ShouldBeInitialized()
    {
        Assert.NotNull(AppMetrics.ActivitySource);
        Assert.Equal("OrderService", AppMetrics.ActivitySource.Name);
        Assert.Equal("1.0.0", AppMetrics.ActivitySource.Version);

        Assert.NotNull(AppMetrics.Meter);
        Assert.Equal("OrderService", AppMetrics.Meter.Name);
        Assert.Equal("1.0.0", AppMetrics.Meter.Version);
    }

    [Fact]
    public void AppMetrics_Counters_ShouldBeInitialized()
    {
        Assert.NotNull(AppMetrics.OrdersCreatedCounter);
        Assert.NotNull(AppMetrics.OrderAmountHistogram);
        Assert.NotNull(AppMetrics.ActiveProcessingOrders);
        Assert.NotNull(AppMetrics.ValkeyCacheHitsCounter);
        Assert.NotNull(AppMetrics.ValkeyCacheMissesCounter);
        Assert.NotNull(AppMetrics.KafkaMessagesPublishedCounter);
        Assert.NotNull(AppMetrics.OpenSearchDocumentsIndexedCounter);
        Assert.NotNull(AppMetrics.PostgresOrdersInsertedCounter);
    }
}
