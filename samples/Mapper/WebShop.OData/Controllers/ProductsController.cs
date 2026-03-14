using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using WebShop.OData.Data;
using WebShop.OData.Mappers;
using WebShop.OData.Projections;

namespace WebShop.OData.Controllers;

/// <summary>OData controller for products — projects via the mapper before OData applies query options.</summary>
public sealed class ProductsController(WebShopDbContext db, WebShopMappers mappers) : ODataController
{
    [EnableQuery]
    public IQueryable<ProductProjection> Get()
    {
        return db.Products
            .Where(p => p.IsActive)
            .Project(mappers.Product);
    }

    [EnableQuery]
    public IQueryable<ProductProjection> Get(Guid key)
    {
        return db.Products
            .Where(p => p.Id == key)
            .Project(mappers.Product);
    }
}
