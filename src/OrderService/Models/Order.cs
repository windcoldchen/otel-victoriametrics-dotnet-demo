namespace OrderService.Models;

public record Order(
    string Id,
    string CustomerId,
    string Product,
    double Amount,
    string Status,
    DateTime CreatedAtUtc);

public record CreateOrderRequest(
    string CustomerId,
    string Product,
    double Amount);
