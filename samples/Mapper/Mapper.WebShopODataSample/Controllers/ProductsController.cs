using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Mapper.WebShopODataSample.Data;
using Mapper.WebShopODataSample.Mappers;
using Mapper.WebShopODataSample.Projections;

namespace Mapper.WebShopODataSample.Controllers;

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
