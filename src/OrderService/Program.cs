using System.Diagnostics;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderService.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// 1. 讀取環境變數配置
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? AppMetrics.ServiceName;
var serviceVersion = AppMetrics.ServiceVersion;

// 2. 定義統一 Resource 標籤 (Semantic Conventions)
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["host.name"] = Environment.MachineName
    });

// 3. 配置 OpenTelemetry Logging (整合 ILogger)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
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

// 4. 配置 OpenTelemetry Metrics & Traces
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(AppMetrics.ServiceName) // 註冊自定義業務指標
            .AddAspNetCoreInstrumentation()   // ASP.NET Core HTTP 請求指標
            .AddHttpClientInstrumentation()   // 出站 HTTP 呼叫指標
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
            .AddSource(AppMetrics.ServiceName) // 註冊自定義鏈路 Activity
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true; // 自動捕捉未處理例外
            })
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
            })
            .AddOtlpExporter(exporterOptions =>
            {
                exporterOptions.Endpoint = new Uri(otlpEndpoint);
            });
    });

// 5. 註冊服務組件
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
