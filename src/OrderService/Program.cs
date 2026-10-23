using System.Diagnostics;
using Confluent.Kafka;
using Npgsql;
using OpenSearch.Client;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderService.Diagnostics;
using OrderService.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. 讀取環境變數與連線配置
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? AppMetrics.ServiceName;
var serviceVersion = AppMetrics.ServiceVersion;

var pgConnectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? builder.Configuration["POSTGRES_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=orderdb;Username=postgres;Password=postgres;";

var valkeyConnectionString = builder.Configuration.GetConnectionString("Valkey")
    ?? builder.Configuration["VALKEY_CONNECTION"]
    ?? "localhost:6379";

var kafkaBootstrapServers = builder.Configuration.GetConnectionString("Kafka")
    ?? builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"]
    ?? "localhost:9092";

var openSearchUrl = builder.Configuration.GetConnectionString("OpenSearch")
    ?? builder.Configuration["OPENSEARCH_URL"]
    ?? "http://localhost:9200";

// 2. 註冊資料與中間件組件 (PostgreSQL, Valkey 9, Kafka 3.9, OpenSearch 3)
// A. PostgreSQL
var npgsqlDataSource = NpgsqlDataSource.Create(pgConnectionString);
builder.Services.AddSingleton(npgsqlDataSource);

// B. Valkey 9 (相容 Redis 協定)
var valkeyOptions = ConfigurationOptions.Parse(valkeyConnectionString);
valkeyOptions.AbortOnConnectFail = false;
valkeyOptions.ConnectTimeout = 3000;
valkeyOptions.SyncTimeout = 3000;
var valkeyMux = ConnectionMultiplexer.Connect(valkeyOptions);
builder.Services.AddSingleton<IConnectionMultiplexer>(valkeyMux);

// C. Apache Kafka 3.9
var producerConfig = new ProducerConfig
{
    BootstrapServers = kafkaBootstrapServers,
    Acks = Acks.Leader,
    MessageTimeoutMs = 3000
};
builder.Services.AddSingleton<IProducer<string, string>>(_ =>
    new ProducerBuilder<string, string>(producerConfig).Build());

// D. OpenSearch 3
var openSearchSettings = new ConnectionSettings(new Uri(openSearchUrl))
    .DefaultIndex("orders")
    .DisableDirectStreaming();
builder.Services.AddSingleton<IOpenSearchClient>(new OpenSearchClient(openSearchSettings));

// E. 背景資料庫與索引初始化器
builder.Services.AddHostedService<DataStoreInitializer>();

// F. 背景 Kafka 分散式鏈路消費者 (串聯 Kafka 鏈路追蹤)
builder.Services.AddHostedService<OrderProcessingConsumer>();

// 3. 定義統一 Resource 標籤 (Semantic Conventions)
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["host.name"] = Environment.MachineName
    });

// 4. 配置 OpenTelemetry Logging (整合 ILogger)
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
});
builder.Logging.AddOpenTelemetry(loggingOptions =>
{
    loggingOptions.SetResourceBuilder(resourceBuilder);
    loggingOptions.IncludeFormattedMessage = true;
    loggingOptions.IncludeScopes = true;
    loggingOptions.ParseStateValues = true;

    // 將結構化日誌外送至 OTel Collector (由 Collector 轉發至 VictoriaLogs)
    loggingOptions.AddOtlpExporter(exporterOptions =>
    {
        exporterOptions.Endpoint = new Uri(otlpEndpoint);
    });
});

// 5. 配置 OpenTelemetry Metrics & Traces (含 PostgreSQL, Valkey, OpenSearch, Kafka 監控)
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .SetExemplarFilter(ExemplarFilterType.TraceBased) // 關鍵：啟用 TraceBased Exemplars 串聯 Metrics ➔ Traces!
            .AddMeter(AppMetrics.ServiceName) // 註冊自定義業務指標
            .AddMeter("Npgsql")               // PostgreSQL 遙測指標
            .AddAspNetCoreInstrumentation()   // ASP.NET Core HTTP 請求指標
            .AddHttpClientInstrumentation()   // 出站 HTTP 呼叫指標 (含 OpenSearch)
            .AddRuntimeInstrumentation()      // .NET 執行階段 (GC, 執行緒, 記憶體) 指標
            .AddOtlpExporter(exporterOptions =>
            {
                exporterOptions.Endpoint = new Uri(otlpEndpoint);
            });
    })
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddSource(AppMetrics.ServiceName) // 註冊自定義鏈路 Activity (含 Kafka)
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true; // 自動捕捉未處理例外
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true; // 自動追蹤 OpenSearch HTTP 呼叫
            })
            .AddNpgsql() // 自動追蹤 PostgreSQL 查詢與交易
            .AddRedisInstrumentation(valkeyMux, options =>
            {
                options.SetVerboseDatabaseStatements = true; // 自動追蹤 Valkey 快取指令
            })
            .AddOtlpExporter(exporterOptions =>
            {
                exporterOptions.Endpoint = new Uri(otlpEndpoint);
            });
    });

// 6. 註冊 Web API 服務組件
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient("ExternalService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
});

var app = builder.Build();

// 6. 中介軟體管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

// 7. 全域 HTTP 請求鏈路追蹤與結構化日誌中介軟體 (確保所有 HTTP 請求均帶有 TraceId Scope 與完成日誌)
app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    var spanId = Activity.Current?.SpanId.ToString() ?? string.Empty;

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["TraceId"] = traceId,
        ["trace_id"] = traceId,
        ["SpanId"] = spanId,
        ["span_id"] = spanId
    }))
    {
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();

        if (!context.Request.Path.StartsWithSegments("/health") &&
            !context.Request.Path.StartsWithSegments("/swagger"))
        {
            app.Logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                traceId);
        }
    }
});

app.MapControllers();

// 健康檢查端點
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = serviceName,
    version = serviceVersion
}));

app.Run();
