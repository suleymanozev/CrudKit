using CrudKit.Api.Extensions;
using CrudKit.Sample.SourceGen.Data;
using CrudKit.Sample.SourceGen.Entities; // brings generated CrudKitEndpoints.MapAllCrudEndpoints() into scope
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>((_, opts) =>
    opts.UseSqlite("Data Source=sourcegen-sample.db"));

builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.UseAuditTrail();
    opts.UseExport();
    opts.UseEnumAsString();
});

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseCrudKit();

// SourceGen magic — one line maps ALL entities discovered via [CrudEntity].
// DTOs, mappers, and endpoint registrations are all auto-generated; zero manual code.
app.MapAllCrudEndpoints();

app.MapOpenApi();
app.MapScalarApiReference();

app.Run();
