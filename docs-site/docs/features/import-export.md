---
sidebar_position: 8
title: Import / Export
---

# Import / Export

CrudKit supports CSV export and import with per-entity and per-property control.

## Entity Setup

```csharp
[CrudEntity(Resource = "products")]
[Exportable]
[Importable]
public class Product : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    [NotExportable]
    public string InternalCode { get; set; } = string.Empty;

    [NotImportable]
    public string CalculatedField { get; set; } = string.Empty;
}
```

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products/export?format=csv` | Export matching records as CSV |
| POST | `/api/products/import` | Import CSV (multipart form upload) |

Export supports the same filters and sort parameters as List:

```
GET /api/products/export?format=csv&price=gte:100&sort=-name
```

## Import Result

```json
{
  "total": 150,
  "created": 142,
  "failed": 8,
  "errors": [
    { "row": 3, "field": "Name", "message": "Name is required." },
    { "row": 7, "field": "Price", "message": "Cannot convert 'abc' to Decimal" }
  ]
}
```

System fields (`Id`, `CreatedAt`, `UpdatedAt`, etc.) are handled automatically during import and should not appear in the CSV.

## Limits

| Option | Default | Description |
|--------|---------|-------------|
| `MaxExportRows` | `50000` | Maximum rows per export request. Excess rows return `400`. |
| `MaxImportFileSize` | `10 MB` | Maximum upload size for CSV import. Larger files return `400`. |

```csharp
opts.UseExport();
opts.MaxExportRows = 50_000;

opts.UseImport();
opts.MaxImportFileSize = 10 * 1024 * 1024;
```

## Feature Flag Levels

| Level | Enable | Disable |
|-------|--------|---------|
| Global | `opts.UseExport()` / `opts.UseImport()` | — (off by default) |
| Entity | `[Exportable]` / `[Importable]` | `[NotExportable]` / `[NotImportable]` |
| Property | — | `[NotExportable]` / `[NotImportable]` |

A property-level `[NotExportable]` excludes that column from the CSV output even when the entity is globally exportable.
