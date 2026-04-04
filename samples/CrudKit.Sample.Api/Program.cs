using CrudKit.Api.Endpoints;
using CrudKit.Api.Extensions;
using CrudKit.Sample.Api.Data;
using CrudKit.Sample.Api.Dtos;
using CrudKit.Sample.Api.Entities;
using Microsoft.EntityFrameworkCore;

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

// OpenAPI
builder.Services.AddOpenApi();

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
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders");
app.MapCrudDetailEndpoints<Order, OrderLine, CreateOrderLine>("orders", "lines", "OrderId");

// Read-only endpoint — only GET /api/units and GET /api/units/{id}
app.MapReadOnlyEndpoints<Unit>("units");

// OpenAPI
app.MapOpenApi();

app.Run();
