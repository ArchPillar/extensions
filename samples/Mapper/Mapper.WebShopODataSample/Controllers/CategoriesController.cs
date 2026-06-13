using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Mapper.WebShopODataSample.Data;
using Mapper.WebShopODataSample.Mappers;
using Mapper.WebShopODataSample.Projections;

namespace Mapper.WebShopODataSample.Controllers;

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
