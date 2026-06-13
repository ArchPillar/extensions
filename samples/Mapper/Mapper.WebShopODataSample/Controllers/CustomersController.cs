using ArchPillar.Extensions.Mapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Mapper.WebShopODataSample.Data;
using Mapper.WebShopODataSample.Mappers;
using Mapper.WebShopODataSample.Projections;

namespace Mapper.WebShopODataSample.Controllers;

/// <summary>OData controller for customers — projects via the mapper before OData applies query options.</summary>
public sealed class CustomersController(WebShopDbContext db, WebShopMappers mappers) : ODataController
{
    [EnableQuery]
    public IQueryable<CustomerProjection> Get()
    {
        return db.Customers.Project(mappers.Customer);
    }

    [EnableQuery]
    public IQueryable<CustomerProjection> Get(Guid key)
    {
        return db.Customers
            .Where(c => c.Id == key)
            .Project(mappers.Customer);
    }
}
