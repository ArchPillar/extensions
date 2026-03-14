using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using WebShop.OData.Data;
using WebShop.OData.Mappers;
using WebShop.OData.Projections;

namespace WebShop.OData.Controllers;

/// <summary>OData controller for categories — projects via the mapper before OData applies query options.</summary>
public sealed class CategoriesController(WebShopDbContext db, WebShopMappers mappers) : ODataController
{
    [EnableQuery]
    public IQueryable<CategoryProjection> Get()
    {
        return db.Categories.Project(mappers.Category);
    }

    [EnableQuery]
    public IQueryable<CategoryProjection> Get(Guid key)
    {
        return db.Categories
            .Where(c => c.Id == key)
            .Project(mappers.Category);
    }
}
