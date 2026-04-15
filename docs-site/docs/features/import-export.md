---
sidebar_position: 10
title: Import / Export
---

# Import / Export

CrudKit supports CSV export and import with per-entity and per-property control.

## Entity Setup

```csharp
[CrudEntity]
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

## CSV Format

**Export output:**
```csv
Name,Price
Widget,29.90
Gadget,49.90
```

**Import file (same format):**
```csv
Name,Price
New Product,19.90
Another Product,39.90
```

## Import Workflow

```bash
# 1. Export existing data as template
curl -o template.csv "https://api.example.com/api/products/export?format=csv"

# 2. Edit the CSV (add new rows, modify values)

# 3. Import
curl -X POST "https://api.example.com/api/products/import" \
  -F "file=@products.csv"
```

```csharp
// C# HttpClient
using var form = new MultipartFormDataContent();
form.Add(new StreamContent(File.OpenRead("products.csv")), "file", "products.csv");

var response = await httpClient.PostAsync("/api/products/import", form);
var result = await response.Content.ReadFromJsonAsync<ImportResult>();
Console.WriteLine($"Created: {result.Created}, Failed: {result.Failed}");
```

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
