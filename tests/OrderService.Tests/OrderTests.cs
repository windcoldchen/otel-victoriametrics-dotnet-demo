using OrderService.Models;
using Xunit;

namespace OrderService.Tests;

public class OrderTests
{
    [Fact]
    public void OrderRecord_ShouldInitializePropertiesCorrectly()
    {
        // Arrange
        var id = "order-123";
        var customerId = "cust-456";
        var product = "Mechanical Keyboard";
        var amount = 129.99;
        var status = "Created";
        var now = DateTime.UtcNow;

        // Act
        var order = new Order(id, customerId, product, amount, status, now);

        // Assert
        Assert.Equal(id, order.Id);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal(product, order.Product);
        Assert.Equal(amount, order.Amount);
        Assert.Equal(status, order.Status);
        Assert.Equal(now, order.CreatedAtUtc);
    }

    [Fact]
    public void CreateOrderRequest_ShouldHoldExpectedValues()
    {
        // Arrange & Act
        var request = new CreateOrderRequest("cust-999", "Mouse", 49.50);

        // Assert
        Assert.Equal("cust-999", request.CustomerId);
        Assert.Equal("Mouse", request.Product);
        Assert.Equal(49.50, request.Amount);
    }
}
