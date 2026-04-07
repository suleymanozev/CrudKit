using CrudKit.Api.Endpoints;
using CrudKit.Api.Extensions;
using CrudKit.Sample.Api.Data;
using CrudKit.Sample.Api.Dtos;
using CrudKit.Sample.Api.Entities;
using CrudKit.Sample.Api.Validators;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<SampleDbContext>((_, opts) =>
    opts.UseSqlite("Data Source=sample.db"));

// CrudKit
builder.Services.AddCrudKit<SampleDbContext>(opts =>
{
    opts.DefaultPageSize = 25;
    opts.MaxPageSize = 100;
    opts.UseAuditTrail();
    opts.UseExport();
    opts.UseEnumAsString();
});

// FluentValidation — takes precedence over DataAnnotation attributes
builder.Services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();
builder.Services.AddScoped<IValidator<CreateOrder>, CreateOrderValidator>();

// OpenAPI — set decimal format to "decimal" instead of "double"
builder.Services.AddOpenApi(opts =>
{
    opts.AddSchemaTransformer((schema, ctx, _) =>
    {
        if (ctx.JsonTypeInfo.Type == typeof(decimal) || ctx.JsonTypeInfo.Type == typeof(decimal?))
            schema.Format = "decimal";
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

// Route-less — route derived from [CrudEntity(Table = ...)]
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>();
app.MapCrudEndpoints<Category, CreateCategory, UpdateCategory>();
// [ChildOf] + [CreateDtoFor] on OrderLine/CreateOrderLine handles List/Get/Delete/POST automatically
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>();
app.MapCrudEndpoints<Unit>();

// OpenAPI + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();
