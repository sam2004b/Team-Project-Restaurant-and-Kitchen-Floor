using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Api.Dtos;
using Restaurant.Api.Models;
using Restaurant.Api.Services.Orders;

namespace Restaurant.Api.Controllers;

[ApiController]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly RestaurantDbContext _db;
    private readonly IOrderAvailabilityService _availabilityService;

    public OrdersController(
        RestaurantDbContext db,
        IOrderAvailabilityService availabilityService)
    {
        _db = db;
        _availabilityService = availabilityService;
    }

    /// <summary>
    /// Place an order for an available dish.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _availabilityService.ValidateAsync(
            [new OrderItemCandidate(request.DishId, request.Quantity)],
            cancellationToken);

        if (!validation.IsValid)
        {
            return validation.Status switch
            {
                OrderValidationStatus.DishUnavailable => Conflict(new
                {
                    code = "dish_unavailable",
                    dishId = validation.DishId,
                    message = "This dish is no longer available. Refresh the menu and choose another dish."
                }),
                OrderValidationStatus.DishNotFound => NotFound(new
                {
                    code = "dish_not_found",
                    dishId = validation.DishId,
                    message = "This dish could not be found. Refresh the menu and choose another dish."
                }),
                _ => BadRequest(new
                {
                    code = "invalid_order",
                    message = "The order must include a valid dish and a positive quantity."
                })
            };
        }

        var dish = await _db.Dishes
            .SingleOrDefaultAsync(
                item => item.Id == request.DishId && item.IsAvailable,
                cancellationToken);

        if (dish is null)
        {
            return Conflict(new
            {
                code = "dish_unavailable",
                dishId = request.DishId,
                message = "This dish is no longer available. Refresh the menu and choose another dish."
            });
        }

        var order = new RestaurantOrder
        {
            CreatedAtUtc = DateTime.UtcNow,
            Items =
            [
                new RestaurantOrderItem
                {
                    DishId = dish.Id,
                    DishName = dish.Name,
                    UnitPrice = dish.Price,
                    Quantity = request.Quantity
                }
            ]
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var response = ToDto(order);
        return CreatedAtAction(
            nameof(GetOrder),
            new { id = order.Id },
            response);
    }

    /// <summary>
    /// Retrieve a previously placed order.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var order = await _db.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        return order is null
            ? NotFound()
            : Ok(ToDto(order));
    }

    private static OrderDto ToDto(RestaurantOrder order) =>
        new(
            order.Id,
            order.CreatedAtUtc,
            order.Items
                .OrderBy(item => item.Id)
                .Select(item => new OrderItemDto(
                    item.DishId,
                    item.DishName,
                    item.UnitPrice,
                    item.Quantity))
                .ToArray());
}
