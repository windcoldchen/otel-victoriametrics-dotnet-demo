using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenSearch.Client;
using OrderService.Models;
using StackExchange.Redis;
using Order = OrderService.Models.Order;

namespace OrderService.Services;

/// <summary>
/// 背景服務：在微服務啟動時自動檢查並初始化 PostgreSQL 表結構、OpenSearch 索引以及 Valkey 快取連接。
/// 包含重試機制，避免在 Docker Compose 啟動初期因相依服務尚未就緒而終止。
/// </summary>
public class DataStoreInitializer : BackgroundService
{
    private readonly NpgsqlDataSource _npgsqlDataSource;
    private readonly IOpenSearchClient _openSearchClient;
    private readonly IConnectionMultiplexer _valkeyMux;
    private readonly ILogger<DataStoreInitializer> _logger;

    public DataStoreInitializer(
        NpgsqlDataSource npgsqlDataSource,
        IOpenSearchClient openSearchClient,
        IConnectionMultiplexer valkeyMux,
        ILogger<DataStoreInitializer> logger)
    {
        _npgsqlDataSource = npgsqlDataSource;
        _openSearchClient = openSearchClient;
        _valkeyMux = valkeyMux;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting DataStoreInitializer for PostgreSQL, OpenSearch, and Valkey...");

        // 1. 初始化 PostgreSQL
        await InitializePostgresAsync(stoppingToken);

        // 2. 初始化 OpenSearch
        await InitializeOpenSearchAsync(stoppingToken);

        // 3. 檢查 Valkey 狀態
        CheckValkeyStatus();
    }

    private async Task InitializePostgresAsync(CancellationToken stoppingToken)
    {
        const int maxRetries = 10;
        for (int i = 1; i <= maxRetries && !stoppingToken.IsCancellationRequested; i++)
        {
            try
            {
                _logger.LogInformation("Verifying PostgreSQL schema (attempt {Attempt}/{MaxRetries})...", i, maxRetries);
                await using var conn = await _npgsqlDataSource.OpenConnectionAsync(stoppingToken);
                await using var cmd = new NpgsqlCommand(
                    """
                    CREATE TABLE IF NOT EXISTS orders (
                        id VARCHAR(64) PRIMARY KEY,
                        customer_id VARCHAR(64) NOT NULL,
                        product VARCHAR(128) NOT NULL,
                        amount NUMERIC(12, 2) NOT NULL,
                        status VARCHAR(32) NOT NULL,
                        created_at_utc TIMESTAMPTZ NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_orders_customer_id ON orders(customer_id);
                    CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at_utc DESC);
                    """, conn);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
                _logger.LogInformation("PostgreSQL 'orders' table verified/created successfully.");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "PostgreSQL not ready yet (attempt {Attempt}/{MaxRetries}). Retrying in 3 seconds...", i, maxRetries);
                try { await Task.Delay(3000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task InitializeOpenSearchAsync(CancellationToken stoppingToken)
    {
        const int maxRetries = 10;
        for (int i = 1; i <= maxRetries && !stoppingToken.IsCancellationRequested; i++)
        {
            try
            {
                _logger.LogInformation("Verifying OpenSearch 'orders' index (attempt {Attempt}/{MaxRetries})...", i, maxRetries);
                var existsResponse = await _openSearchClient.Indices.ExistsAsync("orders", ct: stoppingToken);
                if (!existsResponse.Exists)
                {
                    var createResponse = await _openSearchClient.Indices.CreateAsync("orders", c => c
                        .Map<Order>(m => m.AutoMap()), ct: stoppingToken);
                    _logger.LogInformation("OpenSearch 'orders' index created: {Acknowledged}", createResponse.Acknowledged);
                }
                else
                {
                    _logger.LogInformation("OpenSearch 'orders' index already exists.");
                }
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "OpenSearch not ready yet (attempt {Attempt}/{MaxRetries}). Retrying in 3 seconds...", i, maxRetries);
                try { await Task.Delay(3000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void CheckValkeyStatus()
    {
        try
        {
            if (_valkeyMux.IsConnected)
            {
                _logger.LogInformation("Valkey 9 connection confirmed healthy.");
            }
            else
            {
                _logger.LogWarning("Valkey 9 is currently disconnected; operations will retry on demand.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify Valkey 9 connection.");
        }
    }
}
