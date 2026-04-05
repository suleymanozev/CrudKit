using CrudKit.Api.Endpoints;
using CrudKit.Api.Extensions;
using CrudKit.Sample.Api.Data;
using CrudKit.Sample.Api.Dtos;
using CrudKit.Sample.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<SampleDbContext>((_, opts) =>
    opts.UseSqlite("Data Source=sample.db"));

// CrudKit — single call registers everything
builder.Services.AddCrudKit<SampleDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
});

// OpenAPI — set decimal format to "decimal" instead of "double"
builder.Services.AddOpenApi(opts =>
{
    opts.AddSchemaTransformer((schema, ctx, _) =>
    {
        if (ctx.JsonTypeInfo.Type == typeof(decimal) || ctx.JsonTypeInfo.Type == typeof(decimal?))
        {
            schema.Format = "decimal";
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Ensure database created (development only)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();
}

app.UseCrudKit();

// Map CRUD endpoints
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");
app.MapCrudEndpoints<Category, CreateCategory, UpdateCategory>("categories");
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders")
    .WithDetail<OrderLine, CreateOrderLine>("lines", "OrderId");

// Read-only endpoint — only GET /api/units and GET /api/units/{id}
app.MapCrudEndpoints<Unit>("units");

// OpenAPI + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();
