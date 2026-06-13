using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Operations;
using Microsoft.EntityFrameworkCore;
using Primitives.TypedIdsSample.Catalog;

namespace Primitives.TypedIdsSample.Data;

internal sealed class CatalogStore(CatalogDbContext context)
{
    public async Task<OperationResult<User>> GetUserAsync(Id<UserTag> id)
    {
        // The typed id flows straight into the LINQ predicate; the registered
        // converter translates the comparison to a Guid column filter in SQL.
        User? user = await context.Users.SingleOrDefaultAsync(u => u.Id == id);

        return user is null
            ? OperationResult.NotFound($"User '{id.Value}' was not found.")
            : OperationResult.Ok(user);
    }

    public async Task<OperationResult<Order>> GetOrderByOwnerAsync(Id<UserTag> ownerId)
    {
        // OwnerId is a plain Id<UserTag> property — the auto-convention makes
        // it queryable by typed id with no per-property setup.
        Order? order = await context.Orders.SingleOrDefaultAsync(o => o.OwnerId == ownerId);

        return order is null
            ? OperationResult.NotFound($"No order owned by user '{ownerId.Value}'.")
            : OperationResult.Ok(order);
    }
}
