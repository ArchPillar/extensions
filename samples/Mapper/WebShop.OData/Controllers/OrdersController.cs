using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using WebShop.OData.Data;
using WebShop.OData.Mappers;
using WebShop.OData.Projections;

namespace WebShop.OData.Controllers;

/// <summary>OData controller for orders — projects via the mapper before OData applies query options.</summary>
public sealed class OrdersController(WebShopDbContext db, WebShopMappers mappers) : ODataController
{
    [EnableQuery]
    public IQueryable<OrderProjection> Get()
    {
        return db.Orders.Project(mappers.Order);
    }

    [EnableQuery]
    public IQueryable<OrderProjection> Get(Guid key)
    {
        return db.Orders
            .Where(o => o.Id == key)
            .Project(mappers.Order, opts => opts.Include(o => o.Lines));
    }
}
