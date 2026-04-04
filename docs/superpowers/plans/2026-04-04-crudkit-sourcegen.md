# CrudKit.SourceGen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Roslyn incremental source generator that auto-generates CreateDto, UpdateDto, ResponseDto, and mappers (`IResponseMapper`, `ICreateMapper`, `IUpdateMapper`, `ICrudMapper`) plus endpoint mapping, hook stubs, and DI registration from `[CrudEntity]`-decorated classes.

**Architecture:** `CrudKitSourceGenerator` (IIncrementalGenerator) finds `[CrudEntity]` classes via `ForAttributeWithMetadataName`, parses into `EntityMetadata`, dispatches to focused generators. Each produces `.g.cs` files. Diagnostics catch config errors at compile time.

**Tech Stack:** .NET Standard 2.0, Microsoft.CodeAnalysis.CSharp 4.*, xUnit

---

## File Structure

### Production — `src/CrudKit.SourceGen/`
```
src/CrudKit.SourceGen/
├── CrudKit.SourceGen.csproj
├── CrudKitSourceGenerator.cs
├── Models/
│   ├── EntityMetadata.cs
│   └── PropertyMetadata.cs
├── Parsing/
│   └── EntityParser.cs
├── Diagnostics/
│   └── DiagnosticDescriptors.cs
└── Generators/
    ├── CreateDtoGenerator.cs
    ├── UpdateDtoGenerator.cs
    ├── ResponseDtoGenerator.cs
    ├── MapperGenerator.cs
    ├── HookStubGenerator.cs
    ├── EndpointMappingGenerator.cs
    └── DiRegistrationGenerator.cs
```

### Tests — `tests/CrudKit.SourceGen.Tests/`
```
tests/CrudKit.SourceGen.Tests/
├── CrudKit.SourceGen.Tests.csproj
├── Helpers/
│   └── GeneratorTestHelper.cs
├── Parsing/
│   └── EntityParserTests.cs
├── Diagnostics/
│   └── DiagnosticTests.cs
└── Generators/
    ├── CreateDtoGeneratorTests.cs
    ├── UpdateDtoGeneratorTests.cs
    ├── ResponseDtoGeneratorTests.cs
    ├── MapperGeneratorTests.cs
    ├── HookStubGeneratorTests.cs
    ├── EndpointMappingGeneratorTests.cs
    ├── DiRegistrationGeneratorTests.cs
    └── IntegrationTests.cs
```

---

## Task 1: Project Scaffold

Create `src/CrudKit.SourceGen/CrudKit.SourceGen.csproj`, `tests/CrudKit.SourceGen.Tests/CrudKit.SourceGen.Tests.csproj`, test helper, placeholder generator, and update `CrudKit.slnx`.

### Steps

- [ ] **1.1** Create `src/CrudKit.SourceGen/CrudKit.SourceGen.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>CrudKit.SourceGen</AssemblyName>
    <RootNamespace>CrudKit.SourceGen</RootNamespace>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <!-- Suppress nullable warnings that conflict with netstandard2.0 API surface -->
    <NoWarn>$(NoWarn);RS2008</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <!-- Roslyn SDK — must be PrivateAssets=all so they don't flow to consumers -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **1.2** Create `tests/CrudKit.SourceGen.Tests/CrudKit.SourceGen.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <!-- Roslyn testing infrastructure -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.*" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- Reference generator as an analyzer so the driver picks it up -->
    <ProjectReference Include="..\..\src\CrudKit.SourceGen\CrudKit.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <!-- Also add a direct assembly reference so tests can instantiate the generator -->
    <ProjectReference Include="..\..\src\CrudKit.SourceGen\CrudKit.SourceGen.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **1.3** Create `src/CrudKit.SourceGen/CrudKitSourceGenerator.cs` (placeholder — replaced in Task 10):

```csharp
using Microsoft.CodeAnalysis;

namespace CrudKit.SourceGen;

/// <summary>
/// Roslyn incremental source generator entry point for CrudKit.
/// Finds [CrudEntity]-decorated classes and generates DTOs, mappers, endpoints, and DI registrations.
/// </summary>
[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Placeholder — full wiring added in Task 10.
        // Emits a marker file so the build can verify the generator loads.
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("CrudKit.SourceGen.Marker.g.cs",
                "// CrudKit.SourceGen loaded successfully.\n"));
    }
}
```

- [ ] **1.4** Create `tests/CrudKit.SourceGen.Tests/Helpers/GeneratorTestHelper.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrudKit.SourceGen.Tests.Helpers;

/// <summary>
/// Runs a Roslyn incremental source generator against an in-memory compilation
/// and returns the generated sources and diagnostics.
/// </summary>
public static class GeneratorTestHelper
{
    /// <summary>
    /// Compiles <paramref name="source"/> together with the CrudKit attribute stubs,
    /// runs <typeparamref name="TGenerator"/>, and returns the driver result.
    /// </summary>
    public static GeneratorDriverRunResult RunGenerator<TGenerator>(
        string source,
        string? additionalSource = null)
        where TGenerator : IIncrementalGenerator, new()
    {
        // Build a compilation that includes the source under test plus attribute stubs.
        var sources = new List<string> { AttributeStubs, source };
        if (additionalSource is not null)
            sources.Add(additionalSource);

        var compilation = CreateCompilation(sources.ToArray());

        var generator = new TGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Returns just the generated source text for a specific hint name.
    /// Throws <see cref="InvalidOperationException"/> when the file is not found.
    /// </summary>
    public static string GetGeneratedSource(GeneratorDriverRunResult result, string hintName)
    {
        var file = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith(hintName, StringComparison.OrdinalIgnoreCase));

        if (file is null)
        {
            var available = string.Join(", ", result.GeneratedTrees.Select(t => t.FilePath));
            throw new InvalidOperationException(
                $"Generated file '{hintName}' not found. Available: [{available}]");
        }

        return file.GetText().ToString();
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static CSharpCompilation CreateCompilation(string[] sources)
    {
        var syntaxTrees = sources.Select(s =>
            CSharpSyntaxTree.ParseText(s, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Core .NET references
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll"));
        yield return MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll"));
        yield return MetadataReference.CreateFromFile(
            Assembly.Load("System.ComponentModel.DataAnnotations").Location);
    }

    // ---------------------------------------------------------------------------
    // CrudKit attribute stubs — stand-ins for CrudKit.Core types in tests
    // ---------------------------------------------------------------------------

    internal const string AttributeStubs = """
        using System;
        using System.Linq;
        using System.ComponentModel.DataAnnotations;

        namespace CrudKit.Core.Interfaces
        {
            public interface IEntity
            {
                string Id { get; set; }
                DateTime CreatedAt { get; set; }
                DateTime UpdatedAt { get; set; }
            }

            public interface ISoftDeletable
            {
                DateTime? DeletedAt { get; set; }
            }

            public interface IMultiTenant
            {
                string TenantId { get; set; }
            }

            public interface IResponseMapper<TEntity, TResponse>
                where TEntity : class, IEntity
                where TResponse : class
            {
                TResponse Map(TEntity entity);
                IQueryable<TResponse> Project(IQueryable<TEntity> query);
            }

            public interface ICreateMapper<TEntity, TCreate>
                where TEntity : class, IEntity
                where TCreate : class
            {
                TEntity FromCreateDto(TCreate dto);
            }

            public interface IUpdateMapper<TEntity, TUpdate>
                where TEntity : class, IEntity
                where TUpdate : class
            {
                void ApplyUpdate(TEntity entity, TUpdate dto);
            }

            public interface ICrudMapper<TEntity, TCreate, TUpdate, TResponse>
                : IResponseMapper<TEntity, TResponse>,
                  ICreateMapper<TEntity, TCreate>,
                  IUpdateMapper<TEntity, TUpdate>
                where TEntity : class, IEntity
                where TCreate : class
                where TUpdate : class
                where TResponse : class
            {
            }

            public interface ICrudHooks<T> where T : class, IEntity { }
        }

        namespace CrudKit.Core.Models
        {
            public readonly struct Optional<T>
            {
                public bool HasValue { get; }
                public T? Value { get; }
                public static Optional<T> Undefined => default;
                public static Optional<T> From(T? value) => default;
                public static implicit operator Optional<T>(T? value) => default;
            }
        }

        namespace CrudKit.Core.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public class CrudEntityAttribute : Attribute
            {
                public string Table { get; set; } = string.Empty;
                public bool SoftDelete { get; set; }
                public bool Audit { get; set; }
                public bool MultiTenant { get; set; }
                public string? Workflow { get; set; }
                public string[]? WorkflowProtected { get; set; }
                public string? NumberingPrefix { get; set; }
                public bool NumberingYearlyReset { get; set; } = true;
                public bool EnableBulkUpdate { get; set; }
                public int BulkLimit { get; set; }
                public string? OwnerField { get; set; }
                public bool ReadOnly { get; set; }
                public bool EnableCreate { get; set; } = true;
                public bool EnableUpdate { get; set; } = true;
                public bool EnableDelete { get; set; } = true;
                public bool EnableBulkDelete { get; set; }
                public bool IsCreateEnabled => !ReadOnly && EnableCreate;
                public bool IsUpdateEnabled => !ReadOnly && EnableUpdate;
                public bool IsDeleteEnabled => !ReadOnly && EnableDelete;
            }

            [AttributeUsage(AttributeTargets.Property)] public class HashedAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property)] public class ProtectedAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property)] public class SkipUpdateAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property)] public class SkipResponseAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property)] public class UniqueAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property)] public class SearchableAttribute : Attribute { }
        }
        """;
}
```

- [ ] **1.5** Update `CrudKit.slnx` to include both new projects:

```xml
<Solution>
  <Project Path="src/CrudKit.Core/CrudKit.Core.csproj" />
  <Project Path="src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj" />
  <Project Path="src/CrudKit.Api/CrudKit.Api.csproj" />
  <Project Path="src/CrudKit.SourceGen/CrudKit.SourceGen.csproj" />
  <Project Path="tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj" />
  <Project Path="tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj" />
  <Project Path="tests/CrudKit.Api.Tests/CrudKit.Api.Tests.csproj" />
  <Project Path="tests/CrudKit.SourceGen.Tests/CrudKit.SourceGen.Tests.csproj" />
</Solution>
```

- [ ] **1.6** Create `tests/CrudKit.SourceGen.Tests/Helpers/PlaceholderGeneratorTests.cs` to verify the build:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests;

/// <summary>
/// Verifies the generator loads and emits its marker file.
/// </summary>
public class PlaceholderGeneratorTests
{
    [Fact]
    public void Generator_EmitsMarkerFile()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>("// empty");

        var marker = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Marker"));

        Assert.NotNull(marker);
    }
}
```

---

## Task 2: EntityMetadata + EntityParser

Define the data model that all generators consume, and implement the Roslyn symbol parser.

### Steps

- [ ] **2.1** Create `src/CrudKit.SourceGen/Models/PropertyMetadata.cs`:

```csharp
namespace CrudKit.SourceGen.Models;

/// <summary>
/// Immutable snapshot of a single entity property, extracted from a Roslyn IPropertySymbol.
/// </summary>
internal sealed class PropertyMetadata
{
    public string Name { get; }

    /// <summary>Short type name used in generated code (e.g. "string", "int", "MyType").</summary>
    public string TypeName { get; }

    /// <summary>Fully-qualified type name including namespace.</summary>
    public string FullTypeName { get; }

    /// <summary>True when the property's declared type is nullable (T? or Nullable&lt;T&gt;).</summary>
    public bool IsNullable { get; }

    // Attribute flags
    public bool IsRequired { get; }
    public bool HasMaxLength { get; }
    public int MaxLength { get; }
    public bool HasRange { get; }
    public string RangeMin { get; }
    public string RangeMax { get; }
    public bool IsHashed { get; }
    public bool IsProtected { get; }
    public bool IsSkipUpdate { get; }
    public bool IsSkipResponse { get; }
    public bool IsUnique { get; }
    public bool IsSearchable { get; }

    public PropertyMetadata(
        string name,
        string typeName,
        string fullTypeName,
        bool isNullable,
        bool isRequired,
        bool hasMaxLength,
        int maxLength,
        bool hasRange,
        string rangeMin,
        string rangeMax,
        bool isHashed,
        bool isProtected,
        bool isSkipUpdate,
        bool isSkipResponse,
        bool isUnique,
        bool isSearchable)
    {
        Name = name;
        TypeName = typeName;
        FullTypeName = fullTypeName;
        IsNullable = isNullable;
        IsRequired = isRequired;
        HasMaxLength = hasMaxLength;
        MaxLength = maxLength;
        HasRange = hasRange;
        RangeMin = rangeMin;
        RangeMax = rangeMax;
        IsHashed = isHashed;
        IsProtected = isProtected;
        IsSkipUpdate = isSkipUpdate;
        IsSkipResponse = isSkipResponse;
        IsUnique = isUnique;
        IsSearchable = isSearchable;
    }
}
```

- [ ] **2.2** Create `src/CrudKit.SourceGen/Models/EntityMetadata.cs`:

```csharp
using System.Collections.Generic;

namespace CrudKit.SourceGen.Models;

/// <summary>
/// Immutable snapshot of a [CrudEntity]-decorated class, used by all sub-generators.
/// </summary>
internal sealed class EntityMetadata
{
    public string Name { get; }
    public string Namespace { get; }

    /// <summary>Fully-qualified class name (Namespace.Name).</summary>
    public string FullName { get; }

    /// <summary>Database table name from CrudEntityAttribute.Table.</summary>
    public string Table { get; }

    // CrudEntityAttribute flags
    public bool SoftDelete { get; }
    public bool Audit { get; }
    public bool MultiTenant { get; }
    public bool ReadOnly { get; }
    public bool IsCreateEnabled { get; }
    public bool IsUpdateEnabled { get; }
    public bool IsDeleteEnabled { get; }
    public bool EnableBulkUpdate { get; }
    public bool EnableBulkDelete { get; }
    public string? Workflow { get; }

    // Interface implementation flags (detected from base-type list)
    public bool ImplementsIEntity { get; }
    public bool ImplementsISoftDeletable { get; }
    public bool ImplementsIMultiTenant { get; }

    /// <summary>
    /// All declared properties excluding system fields (Id, CreatedAt, UpdatedAt, DeletedAt, TenantId, RowVersion).
    /// </summary>
    public IReadOnlyList<PropertyMetadata> Properties { get; }

    public EntityMetadata(
        string name,
        string @namespace,
        string fullName,
        string table,
        bool softDelete,
        bool audit,
        bool multiTenant,
        bool readOnly,
        bool isCreateEnabled,
        bool isUpdateEnabled,
        bool isDeleteEnabled,
        bool enableBulkUpdate,
        bool enableBulkDelete,
        string? workflow,
        bool implementsIEntity,
        bool implementsISoftDeletable,
        bool implementsIMultiTenant,
        IReadOnlyList<PropertyMetadata> properties)
    {
        Name = name;
        Namespace = @namespace;
        FullName = fullName;
        Table = table;
        SoftDelete = softDelete;
        Audit = audit;
        MultiTenant = multiTenant;
        ReadOnly = readOnly;
        IsCreateEnabled = isCreateEnabled;
        IsUpdateEnabled = isUpdateEnabled;
        IsDeleteEnabled = isDeleteEnabled;
        EnableBulkUpdate = enableBulkUpdate;
        EnableBulkDelete = enableBulkDelete;
        Workflow = workflow;
        ImplementsIEntity = implementsIEntity;
        ImplementsISoftDeletable = implementsISoftDeletable;
        ImplementsIMultiTenant = implementsIMultiTenant;
        Properties = properties;
    }
}
```

- [ ] **2.3** Create `src/CrudKit.SourceGen/Parsing/EntityParser.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Parsing;

/// <summary>
/// Extracts <see cref="EntityMetadata"/> from a Roslyn <see cref="INamedTypeSymbol"/>
/// decorated with [CrudEntity].
/// </summary>
internal static class EntityParser
{
    // System fields automatically excluded from generated DTOs.
    private static readonly HashSet<string> SystemFields = new HashSet<string>(System.StringComparer.Ordinal)
    {
        "Id", "CreatedAt", "UpdatedAt", "DeletedAt", "TenantId", "RowVersion"
    };

    private const string CrudEntityAttributeFqn = "CrudKit.Core.Attributes.CrudEntityAttribute";
    private const string IEntityFqn              = "CrudKit.Core.Interfaces.IEntity";
    private const string ISoftDeletableFqn       = "CrudKit.Core.Interfaces.ISoftDeletable";
    private const string IMultiTenantFqn         = "CrudKit.Core.Interfaces.IMultiTenant";

    /// <summary>
    /// Parses a [CrudEntity] class symbol into an <see cref="EntityMetadata"/> snapshot.
    /// Returns <c>null</c> when the attribute is missing.
    /// </summary>
    public static EntityMetadata? Parse(INamedTypeSymbol classSymbol)
    {
        var attr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudEntityAttributeFqn);

        if (attr is null)
            return null;

        var attrArgs = BuildAttributeArguments(attr);

        string name      = classSymbol.Name;
        string ns        = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        string fullName  = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        string table     = GetString(attrArgs, "Table", name + "s");
        bool softDelete  = GetBool(attrArgs, "SoftDelete");
        bool audit       = GetBool(attrArgs, "Audit");
        bool multiTenant = GetBool(attrArgs, "MultiTenant");
        bool readOnly    = GetBool(attrArgs, "ReadOnly");
        bool enableCreate  = GetBool(attrArgs, "EnableCreate", defaultValue: true);
        bool enableUpdate  = GetBool(attrArgs, "EnableUpdate", defaultValue: true);
        bool enableDelete  = GetBool(attrArgs, "EnableDelete", defaultValue: true);
        bool bulkUpdate  = GetBool(attrArgs, "EnableBulkUpdate");
        bool bulkDelete  = GetBool(attrArgs, "EnableBulkDelete");
        string? workflow = GetNullableString(attrArgs, "Workflow");

        bool implementsIEntity       = ImplementsInterface(classSymbol, IEntityFqn);
        bool implementsISoftDeletable = ImplementsInterface(classSymbol, ISoftDeletableFqn);
        bool implementsIMultiTenant  = ImplementsInterface(classSymbol, IMultiTenantFqn);

        var properties = ParseProperties(classSymbol);

        return new EntityMetadata(
            name: name,
            @namespace: ns,
            fullName: fullName,
            table: table,
            softDelete: softDelete,
            audit: audit,
            multiTenant: multiTenant,
            readOnly: readOnly,
            isCreateEnabled: !readOnly && enableCreate,
            isUpdateEnabled: !readOnly && enableUpdate,
            isDeleteEnabled: !readOnly && enableDelete,
            enableBulkUpdate: bulkUpdate,
            enableBulkDelete: bulkDelete,
            workflow: workflow,
            implementsIEntity: implementsIEntity,
            implementsISoftDeletable: implementsISoftDeletable,
            implementsIMultiTenant: implementsIMultiTenant,
            properties: properties);
    }

    // ---------------------------------------------------------------------------
    // Property parsing
    // ---------------------------------------------------------------------------

    private static IReadOnlyList<PropertyMetadata> ParseProperties(INamedTypeSymbol classSymbol)
    {
        var result = new List<PropertyMetadata>();

        // Walk the declared members (not inherited) and include inherited IEntity props via AllInterfaces.
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (SystemFields.Contains(prop.Name))
                continue;

            result.Add(ParseProperty(prop));
        }

        return result;
    }

    private static PropertyMetadata ParseProperty(IPropertySymbol prop)
    {
        bool isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated
                          || IsNullableValueType(prop.Type);

        string typeName     = GetTypeName(prop.Type);
        string fullTypeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Attribute flags
        bool isRequired    = HasAttribute(prop, "System.ComponentModel.DataAnnotations.RequiredAttribute");
        bool isHashed      = HasAttribute(prop, "CrudKit.Core.Attributes.HashedAttribute");
        bool isProtected   = HasAttribute(prop, "CrudKit.Core.Attributes.ProtectedAttribute");
        bool isSkipUpdate  = HasAttribute(prop, "CrudKit.Core.Attributes.SkipUpdateAttribute");
        bool isSkipResponse = HasAttribute(prop, "CrudKit.Core.Attributes.SkipResponseAttribute");
        bool isUnique      = HasAttribute(prop, "CrudKit.Core.Attributes.UniqueAttribute");
        bool isSearchable  = HasAttribute(prop, "CrudKit.Core.Attributes.SearchableAttribute");

        // [MaxLength(n)]
        bool hasMaxLength = false;
        int  maxLength    = 0;
        var  mlAttr       = GetAttribute(prop, "System.ComponentModel.DataAnnotations.MaxLengthAttribute");
        if (mlAttr != null && mlAttr.ConstructorArguments.Length > 0)
        {
            hasMaxLength = true;
            maxLength    = (int)(mlAttr.ConstructorArguments[0].Value ?? 0);
        }

        // [Range(min, max)]
        bool   hasRange  = false;
        string rangeMin  = "0";
        string rangeMax  = "0";
        var    rangeAttr = GetAttribute(prop, "System.ComponentModel.DataAnnotations.RangeAttribute");
        if (rangeAttr != null && rangeAttr.ConstructorArguments.Length >= 2)
        {
            hasRange = true;
            rangeMin = rangeAttr.ConstructorArguments[0].Value?.ToString() ?? "0";
            rangeMax = rangeAttr.ConstructorArguments[1].Value?.ToString() ?? "0";
        }

        return new PropertyMetadata(
            name: prop.Name,
            typeName: typeName,
            fullTypeName: fullTypeName,
            isNullable: isNullable,
            isRequired: isRequired,
            hasMaxLength: hasMaxLength,
            maxLength: maxLength,
            hasRange: hasRange,
            rangeMin: rangeMin,
            rangeMax: rangeMax,
            isHashed: isHashed,
            isProtected: isProtected,
            isSkipUpdate: isSkipUpdate,
            isSkipResponse: isSkipResponse,
            isUnique: isUnique,
            isSearchable: isSearchable);
    }

    // ---------------------------------------------------------------------------
    // Symbol helpers
    // ---------------------------------------------------------------------------

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string fqn)
    {
        return symbol.AllInterfaces.Any(i => i.ToDisplayString() == fqn);
    }

    private static bool HasAttribute(ISymbol symbol, string fqn)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fqn);

    private static AttributeData? GetAttribute(ISymbol symbol, string fqn)
        => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fqn);

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        // Strip trailing ? from nullable reference types for the base name
        return type.WithNullableAnnotation(NullableAnnotation.None)
                   .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    // ---------------------------------------------------------------------------
    // Attribute argument helpers
    // ---------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildAttributeArguments(AttributeData attr)
    {
        var dict = new Dictionary<string, object?>(System.StringComparer.Ordinal);

        // Named arguments take precedence
        foreach (var kv in attr.NamedArguments)
            dict[kv.Key] = kv.Value.Value;

        return dict;
    }

    private static bool GetBool(Dictionary<string, object?> args, string key, bool defaultValue = false)
        => args.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;

    private static string GetString(Dictionary<string, object?> args, string key, string defaultValue)
        => args.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s) ? s : defaultValue;

    private static string? GetNullableString(Dictionary<string, object?> args, string key)
        => args.TryGetValue(key, out var v) ? v as string : null;
}
```

- [ ] **2.4** Create `tests/CrudKit.SourceGen.Tests/Parsing/EntityParserTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Parsing;

/// <summary>
/// Verifies EntityParser extracts metadata correctly from [CrudEntity] symbols.
/// Tests run the full generator pipeline and inspect what was generated, acting
/// as an indirect integration test for the parser.
/// </summary>
public class EntityParserTests
{
    private const string BasicEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace MyApp.Entities
        {
            [CrudEntity(Table = "Products", SoftDelete = true, MultiTenant = true)]
            public class Product : IEntity, ISoftDeletable, IMultiTenant
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
                public string TenantId { get; set; } = string.Empty;

                [Required]
                [MaxLength(200)]
                public string Name { get; set; } = string.Empty;

                [Range(0.01, 999999.99)]
                public decimal Price { get; set; }

                [SkipUpdate]
                public string Sku { get; set; } = string.Empty;

                [SkipResponse]
                public string InternalNote { get; set; } = string.Empty;

                [Protected]
                public string OwnerId { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void Parser_GeneratesCreateDto_ForEntityWithRequiredProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(BasicEntity);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");
        Assert.Contains("CreateProduct", source);
        Assert.Contains("string Name", source);
        Assert.Contains("[Required]", source);
        Assert.Contains("[MaxLength(200)]", source);
    }

    [Fact]
    public void Parser_SystemFieldsExcluded_FromCreateDto()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(BasicEntity);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");
        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
        Assert.DoesNotContain("DeletedAt", source);
        Assert.DoesNotContain("TenantId", source);
    }

    [Fact]
    public void Parser_SkipUpdateProps_ExcludedFromUpdateDto()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(BasicEntity);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductUpdateDto.g.cs");
        Assert.DoesNotContain("Sku", source);
    }

    [Fact]
    public void Parser_SkipResponseProps_ExcludedFromResponseDto()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(BasicEntity);

        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductResponseDto.g.cs");
        Assert.DoesNotContain("InternalNote", source);
    }

    [Fact]
    public void Parser_ReadOnlyEntity_ProducesNoCreateOrUpdateDto()
    {
        const string readonlyEntity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace MyApp.Entities
            {
                [CrudEntity(Table = "ReadModels", ReadOnly = true)]
                public class ReadModel : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(readonlyEntity);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.Contains("CreateDto"));
        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.Contains("UpdateDto"));
    }
}
```

---

## Task 3: DiagnosticDescriptors

Define compile-time diagnostics and the validation logic that emits them.

### Steps

- [ ] **3.1** Create `src/CrudKit.SourceGen/Diagnostics/DiagnosticDescriptors.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace CrudKit.SourceGen.Diagnostics;

/// <summary>
/// Compile-time diagnostic descriptors emitted by CrudKitSourceGenerator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "CrudKit";

    /// <summary>
    /// CRUD001: A [CrudEntity] class does not implement IEntity.
    /// Without IEntity the generated code cannot compile — hard error.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingIEntity = new DiagnosticDescriptor(
        id: "CRUD001",
        title: "Entity does not implement IEntity",
        messageFormat: "'{0}' is decorated with [CrudEntity] but does not implement CrudKit.Core.Interfaces.IEntity. Implement IEntity to use CrudKit source generation.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD001");

    /// <summary>
    /// CRUD002: SoftDelete=true but the entity does not implement ISoftDeletable.
    /// The generator will still produce code, but EF Core soft-delete will not work — warning.
    /// </summary>
    public static readonly DiagnosticDescriptor SoftDeleteWithoutISoftDeletable = new DiagnosticDescriptor(
        id: "CRUD002",
        title: "SoftDelete enabled but ISoftDeletable not implemented",
        messageFormat: "'{0}' has SoftDelete=true but does not implement CrudKit.Core.Interfaces.ISoftDeletable. Add ISoftDeletable or set SoftDelete=false.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD002");

    /// <summary>
    /// CRUD003: MultiTenant=true but the entity does not implement IMultiTenant.
    /// </summary>
    public static readonly DiagnosticDescriptor MultiTenantWithoutIMultiTenant = new DiagnosticDescriptor(
        id: "CRUD003",
        title: "MultiTenant enabled but IMultiTenant not implemented",
        messageFormat: "'{0}' has MultiTenant=true but does not implement CrudKit.Core.Interfaces.IMultiTenant. Add IMultiTenant or set MultiTenant=false.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD003");

    /// <summary>
    /// CRUD010: CrudEntityAttribute.Table is explicitly set to an empty string.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyTableName = new DiagnosticDescriptor(
        id: "CRUD010",
        title: "CrudEntity Table name is empty",
        messageFormat: "'{0}' has an empty Table name in [CrudEntity]. Provide a non-empty table name or omit it to use the default (entity name + 's').",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://crudkit.dev/diagnostics/CRUD010");
}
```

- [ ] **3.2** Create `src/CrudKit.SourceGen/Parsing/EntityValidator.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Parsing;

/// <summary>
/// Validates an <see cref="EntityMetadata"/> snapshot and returns any diagnostics.
/// The generator emits these diagnostics via the <see cref="SourceProductionContext"/>.
/// </summary>
internal static class EntityValidator
{
    /// <summary>
    /// Returns all diagnostics for the given entity metadata.
    /// <paramref name="location"/> should be the [CrudEntity] attribute syntax location.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Validate(
        EntityMetadata metadata,
        Location location)
    {
        var diagnostics = new List<Diagnostic>();

        // CRUD001 — must implement IEntity
        if (!metadata.ImplementsIEntity)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MissingIEntity,
                location,
                metadata.Name));
        }

        // CRUD002 — SoftDelete without ISoftDeletable
        if (metadata.SoftDelete && !metadata.ImplementsISoftDeletable)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.SoftDeleteWithoutISoftDeletable,
                location,
                metadata.Name));
        }

        // CRUD003 — MultiTenant without IMultiTenant
        if (metadata.MultiTenant && !metadata.ImplementsIMultiTenant)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MultiTenantWithoutIMultiTenant,
                location,
                metadata.Name));
        }

        // CRUD010 — explicit empty table name (default is auto-derived, so empty = user set it wrong)
        // We detect this by checking: attribute has Table= but it's empty.
        // EntityParser defaults to "EntityName + s" when Table is empty/omitted, so we need to
        // re-read the raw attribute here. This check is done in the generator before Parse().
        // See EntityParser.Parse() — if you get an EntityMetadata back, Table is never empty.
        // CRUD010 is raised in CrudKitSourceGenerator.cs before calling Parse() (see Task 10).

        return diagnostics;
    }
}
```

- [ ] **3.3** Create `tests/CrudKit.SourceGen.Tests/Diagnostics/DiagnosticTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Diagnostics;

/// <summary>
/// Verifies that CRUD001–CRUD010 diagnostics are raised for invalid entities.
/// </summary>
public class DiagnosticTests
{
    [Fact]
    public void CRUD001_RaisedWhen_EntityDoesNotImplementIEntity()
    {
        const string source = """
            using CrudKit.Core.Attributes;

            namespace MyApp
            {
                [CrudEntity(Table = "Widgets")]
                public class Widget
                {
                    public string Id { get; set; } = string.Empty;
                    public string Name { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD001");
    }

    [Fact]
    public void CRUD002_RaisedWhen_SoftDeleteTrueButNoISoftDeletable()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace MyApp
            {
                [CrudEntity(Table = "Widgets", SoftDelete = true)]
                public class Widget : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD002");
    }

    [Fact]
    public void CRUD003_RaisedWhen_MultiTenantTrueButNoIMultiTenant()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace MyApp
            {
                [CrudEntity(Table = "Widgets", MultiTenant = true)]
                public class Widget : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD003");
    }

    [Fact]
    public void CRUD010_RaisedWhen_TableNameIsExplicitlyEmpty()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace MyApp
            {
                [CrudEntity(Table = "")]
                public class Widget : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "CRUD010");
    }

    [Fact]
    public void NoDiagnostics_ForValidEntity()
    {
        const string source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace MyApp
            {
                [CrudEntity(Table = "Widgets")]
                public class Widget : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Name { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(source);

        var crudDiagnostics = result.Diagnostics
            .Where(d => d.Id.StartsWith("CRUD"))
            .ToList();
        Assert.Empty(crudDiagnostics);
    }
}
```

---

## Task 4: CreateDtoGenerator

Generates a positional record `Create{Name}` with validation attributes, skipping system fields. Emits nothing when `IsCreateEnabled` is false.

### Steps

- [ ] **4.1** Create `src/CrudKit.SourceGen/Generators/CreateDtoGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates a <c>Create{Name}</c> positional record DTO from entity metadata.
/// Returns <c>null</c> when the entity is ReadOnly or EnableCreate is false.
/// </summary>
internal static class CreateDtoGenerator
{
    /// <summary>
    /// Builds the source text for the CreateDto file.
    /// Returns <c>null</c> when no file should be emitted.
    /// </summary>
    public static string? Generate(EntityMetadata entity)
    {
        if (!entity.IsCreateEnabled)
            return null;

        // Properties eligible for CreateDto: not system, not [Protected]
        var props = new List<PropertyMetadata>();
        foreach (var p in entity.Properties)
        {
            if (!p.IsProtected)
                props.Add(p);
        }

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine();
        sb.AppendLine($"namespace {entity.Namespace}.Dtos;");
        sb.AppendLine();
        sb.Append($"public sealed record Create{entity.Name}(");

        if (props.Count == 0)
        {
            sb.AppendLine(");");
        }
        else
        {
            sb.AppendLine();
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                bool isLast = i == props.Count - 1;

                // Emit validation attributes on the parameter
                if (p.IsRequired)
                    sb.AppendLine("    [Required]");
                if (p.HasMaxLength)
                    sb.AppendLine($"    [MaxLength({p.MaxLength})]");
                if (p.HasRange)
                    sb.AppendLine($"    [Range({p.RangeMin}, {p.RangeMax})]");

                string typeName = p.IsNullable && !p.TypeName.EndsWith("?")
                    ? p.TypeName + "?"
                    : p.TypeName;

                string defaultVal = GetDefaultValue(p);
                sb.Append($"    {typeName} {p.Name}");
                if (!string.IsNullOrEmpty(defaultVal))
                    sb.Append($" = {defaultVal}");

                if (!isLast)
                    sb.AppendLine(",");
                else
                    sb.AppendLine(");");
            }
        }

        return sb.ToString();
    }

    // Returns a sensible default expression so the record compiles even when not all
    // parameters are required.  Required props get no default.
    private static string GetDefaultValue(PropertyMetadata p)
    {
        if (p.IsRequired)
            return string.Empty;

        if (p.IsNullable)
            return "null";

        // Value-type defaults
        switch (p.TypeName)
        {
            case "string": return "\"\"";
            case "int":
            case "long":
            case "short":
            case "byte":
            case "decimal":
            case "float":
            case "double": return "default";
            case "bool": return "false";
            case "Guid": return "default";
            default: return "default";
        }
    }
}
```

- [ ] **4.2** Create `tests/CrudKit.SourceGen.Tests/Generators/CreateDtoGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class CreateDtoGeneratorTests
{
    private const string ProductEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Store.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }

                [Required]
                [MaxLength(200)]
                public string Name { get; set; } = string.Empty;

                [Range(0.01, 99999.99)]
                public decimal Price { get; set; }

                public string? Description { get; set; }

                [Protected]
                public string InternalCode { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void CreateDto_ContainsRequiredProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.Contains("CreateProduct", source);
        Assert.Contains("[Required]", source);
        Assert.Contains("[MaxLength(200)]", source);
        Assert.Contains("decimal Price", source);
        Assert.Contains("string? Description", source);
    }

    [Fact]
    public void CreateDto_ExcludesProtectedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.DoesNotContain("InternalCode", source);
    }

    [Fact]
    public void CreateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
        Assert.DoesNotContain(" Id ", source);
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenEnableCreateFalse()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Logs", EnableCreate = false)]
                public class Log : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Message { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateDto"));
    }

    [Fact]
    public void CreateDto_NotGenerated_WhenReadOnly()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Views", ReadOnly = true)]
                public class View : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("CreateDto"));
    }

    [Fact]
    public void CreateDto_HasRangeAttribute_WhenPropertyHasRange()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ProductEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        Assert.Contains("[Range(0.01, 99999.99)]", source);
    }
}
```

---

## Task 5: UpdateDtoGenerator

Generates a record `Update{Name}` with `Optional<T>` properties. Excludes `[SkipUpdate]`, `[Protected]`, and system fields. Emits nothing when `IsUpdateEnabled` is false.

### Steps

- [ ] **5.1** Create `src/CrudKit.SourceGen/Generators/UpdateDtoGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates an <c>Update{Name}</c> positional record DTO with <c>Optional&lt;T&gt;</c> wrapping.
/// Returns <c>null</c> when the entity has updates disabled.
/// </summary>
internal static class UpdateDtoGenerator
{
    /// <summary>
    /// Builds the source text for the UpdateDto file.
    /// Returns <c>null</c> when no file should be emitted.
    /// </summary>
    public static string? Generate(EntityMetadata entity)
    {
        if (!entity.IsUpdateEnabled)
            return null;

        // Properties eligible for UpdateDto: not system, not [Protected], not [SkipUpdate]
        var props = new List<PropertyMetadata>();
        foreach (var p in entity.Properties)
        {
            if (!p.IsProtected && !p.IsSkipUpdate)
                props.Add(p);
        }

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using CrudKit.Core.Models;");
        sb.AppendLine();
        sb.AppendLine($"namespace {entity.Namespace}.Dtos;");
        sb.AppendLine();
        sb.Append($"public sealed record Update{entity.Name}(");

        if (props.Count == 0)
        {
            sb.AppendLine(");");
        }
        else
        {
            sb.AppendLine();
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                bool isLast = i == props.Count - 1;

                // Wrap in Optional<T> — nullable types stay nullable inside Optional
                string innerType = p.IsNullable && !p.TypeName.EndsWith("?")
                    ? p.TypeName + "?"
                    : p.TypeName;

                sb.Append($"    Optional<{innerType}> {p.Name} = default");

                if (!isLast)
                    sb.AppendLine(",");
                else
                    sb.AppendLine(");");
            }
        }

        return sb.ToString();
    }
}
```

- [ ] **5.2** Create `tests/CrudKit.SourceGen.Tests/Generators/UpdateDtoGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class UpdateDtoGeneratorTests
{
    private const string OrderEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Store.Entities
        {
            [CrudEntity(Table = "Orders")]
            public class Order : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }

                [Required]
                public string CustomerName { get; set; } = string.Empty;

                [SkipUpdate]
                public string OrderNumber { get; set; } = string.Empty;

                [Protected]
                public string CreatedById { get; set; } = string.Empty;

                public decimal Total { get; set; }
                public string? Notes { get; set; }
            }
        }
        """;

    [Fact]
    public void UpdateDto_ContainsOptionalWrappedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.Contains("UpdateOrder", source);
        Assert.Contains("Optional<string> CustomerName", source);
        Assert.Contains("Optional<decimal> Total", source);
        Assert.Contains("Optional<string?> Notes", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSkipUpdateProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("OrderNumber", source);
    }

    [Fact]
    public void UpdateDto_ExcludesProtectedProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void UpdateDto_ExcludesSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("UpdatedAt", source);
    }

    [Fact]
    public void UpdateDto_HasDefaultValues_ForOptionalParams()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        // Each parameter should default to Optional<T>.Undefined (default)
        Assert.Contains("= default", source);
    }

    [Fact]
    public void UpdateDto_NotGenerated_WhenEnableUpdateFalse()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Immutable", EnableUpdate = false)]
                public class ImmutableRecord : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.Contains("UpdateDto"));
    }

    [Fact]
    public void UpdateDto_ImportsOptionalNamespace()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(OrderEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "OrderUpdateDto.g.cs");

        Assert.Contains("using CrudKit.Core.Models;", source);
    }
}
```

---

## Task 6: ResponseDtoGenerator

Generates a `{Name}Response` record excluding `[SkipResponse]` fields. Always generated (even for ReadOnly entities).

### Steps

- [ ] **6.1** Create `src/CrudKit.SourceGen/Generators/ResponseDtoGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates a <c>{Name}Response</c> positional record DTO.
/// Always emitted — even for ReadOnly entities.
/// Excludes properties marked with [SkipResponse].
/// Includes system fields (Id, CreatedAt, UpdatedAt) and optional soft-delete/tenant fields.
/// </summary>
internal static class ResponseDtoGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        // Start with system fields that belong in the response
        var responseProps = new List<(string Type, string Name)>
        {
            ("string", "Id"),
            ("DateTime", "CreatedAt"),
            ("DateTime", "UpdatedAt"),
        };

        if (entity.ImplementsISoftDeletable)
            responseProps.Add(("DateTime?", "DeletedAt"));

        if (entity.ImplementsIMultiTenant)
            responseProps.Add(("string", "TenantId"));

        // Add user-defined properties, excluding [SkipResponse]
        foreach (var p in entity.Properties)
        {
            if (p.IsSkipResponse)
                continue;

            string typeName = p.IsNullable && !p.TypeName.EndsWith("?")
                ? p.TypeName + "?"
                : p.TypeName;

            responseProps.Add((typeName, p.Name));
        }

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine($"namespace {entity.Namespace}.Dtos;");
        sb.AppendLine();
        sb.Append($"public sealed record {entity.Name}Response(");
        sb.AppendLine();

        for (int i = 0; i < responseProps.Count; i++)
        {
            var (type, name) = responseProps[i];
            bool isLast = i == responseProps.Count - 1;

            sb.Append($"    {type} {name}");
            if (!isLast)
                sb.AppendLine(",");
            else
                sb.AppendLine(");");
        }

        return sb.ToString();
    }
}
```

- [ ] **6.2** Create `tests/CrudKit.SourceGen.Tests/Generators/ResponseDtoGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class ResponseDtoGeneratorTests
{
    private const string CategoryEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace Store.Entities
        {
            [CrudEntity(Table = "Categories", SoftDelete = true)]
            public class Category : IEntity, ISoftDeletable
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }

                public string Name { get; set; } = string.Empty;

                [SkipResponse]
                public string InternalTag { get; set; } = string.Empty;

                public int SortOrder { get; set; }
            }
        }
        """;

    [Fact]
    public void ResponseDto_ContainsSystemFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("string Id", source);
        Assert.Contains("DateTime CreatedAt", source);
        Assert.Contains("DateTime UpdatedAt", source);
    }

    [Fact]
    public void ResponseDto_ContainsDeletedAt_WhenSoftDeletable()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("DateTime? DeletedAt", source);
    }

    [Fact]
    public void ResponseDto_ExcludesSkipResponseProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.DoesNotContain("InternalTag", source);
    }

    [Fact]
    public void ResponseDto_IncludesUserProps()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(CategoryEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CategoryResponseDto.g.cs");

        Assert.Contains("string Name", source);
        Assert.Contains("int SortOrder", source);
    }

    [Fact]
    public void ResponseDto_AlwaysGenerated_ForReadOnlyEntity()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "ReadViews", ReadOnly = true)]
                public class ReadView : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Label { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);

        var responseDtoTree = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("ResponseDto"));

        Assert.NotNull(responseDtoTree);
    }

    [Fact]
    public void ResponseDto_IncludesTenantId_WhenMultiTenant()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Store.Entities
            {
                [CrudEntity(Table = "Tenanted", MultiTenant = true)]
                public class Tenanted : IEntity, IMultiTenant
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string TenantId { get; set; } = string.Empty;
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "TenantedResponseDto.g.cs");

        Assert.Contains("string TenantId", source);
    }
}
```

---

## Task 7: MapperGenerator

Generates a mapper class that implements the appropriate interface(s) depending on which operations are enabled:
- All enabled → `ICrudMapper<TEntity, TCreate, TUpdate, TResponse>` (implements all three)
- ReadOnly → only `IResponseMapper<TEntity, TResponse>`
- EnableCreate=false → `IResponseMapper` + `IUpdateMapper`
- EnableUpdate=false → `IResponseMapper` + `ICreateMapper`

Methods generated:
- `TResponse Map(TEntity entity)` — from `IResponseMapper`
- `IQueryable<TResponse> Project(IQueryable<TEntity> query)` — from `IResponseMapper`
- `TEntity FromCreateDto(TCreate dto)` — from `ICreateMapper` (omitted when create disabled)
- `void ApplyUpdate(TEntity entity, TUpdate dto)` — from `IUpdateMapper` (omitted when update disabled)

### Steps

- [ ] **7.1** Create `src/CrudKit.SourceGen/Generators/MapperGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates <c>{Name}Mapper</c> implementing the appropriate mapper interface(s):
/// <list type="bullet">
///   <item>All CRUD enabled → <c>ICrudMapper&lt;TEntity, TCreate, TUpdate, TResponse&gt;</c></item>
///   <item>ReadOnly → <c>IResponseMapper&lt;TEntity, TResponse&gt;</c></item>
///   <item>Create disabled → <c>IResponseMapper</c> + <c>IUpdateMapper</c></item>
///   <item>Update disabled → <c>IResponseMapper</c> + <c>ICreateMapper</c></item>
/// </list>
/// </summary>
internal static class MapperGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        bool hasCreate = entity.IsCreateEnabled;
        bool hasUpdate = entity.IsUpdateEnabled;

        // Determine which interface(s) to implement
        string interfaceList = BuildInterfaceList(entity, hasCreate, hasUpdate);

        var responseProps = BuildResponseProps(entity);
        var assignments   = BuildAssignments(responseProps);

        // Properties for create/update DTOs (mirrors CreateDtoGenerator/UpdateDtoGenerator logic)
        var createProps = BuildCreateProps(entity);
        var updateProps = BuildUpdateProps(entity);

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Linq;");
        sb.AppendLine($"using {entity.Namespace}.Dtos;");
        sb.AppendLine($"using {entity.Namespace}.Entities;");
        sb.AppendLine("using CrudKit.Core.Interfaces;");
        sb.AppendLine();
        sb.AppendLine($"namespace {entity.Namespace}.Mappers;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>Mapper for <see cref=\"{entity.Name}\"/>.</summary>");
        sb.AppendLine($"public sealed class {entity.Name}Mapper : {interfaceList}");
        sb.AppendLine("{");

        // --- IResponseMapper: Map() ---
        sb.AppendLine($"    public {entity.Name}Response Map({entity.Name} entity)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return new {entity.Name}Response(");

        for (int i = 0; i < assignments.Count; i++)
        {
            bool isLast = i == assignments.Count - 1;
            sb.Append($"            entity.{assignments[i]}");
            if (!isLast)
                sb.AppendLine(",");
            else
                sb.AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        // --- IResponseMapper: Project() ---
        sb.AppendLine($"    public IQueryable<{entity.Name}Response> Project(IQueryable<{entity.Name}> query)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return query.Select(entity => new {entity.Name}Response(");

        for (int i = 0; i < assignments.Count; i++)
        {
            bool isLast = i == assignments.Count - 1;
            sb.Append($"            entity.{assignments[i]}");
            if (!isLast)
                sb.AppendLine(",");
            else
                sb.AppendLine("));");
        }

        sb.AppendLine("    }");

        // --- ICreateMapper: FromCreateDto() ---
        if (hasCreate)
        {
            sb.AppendLine();
            sb.AppendLine($"    public {entity.Name} FromCreateDto(Create{entity.Name} dto)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return new {entity.Name}");
            sb.AppendLine("        {");

            foreach (var p in createProps)
                sb.AppendLine($"            {p} = dto.{p},");

            sb.AppendLine("        };");
            sb.AppendLine("    }");
        }

        // --- IUpdateMapper: ApplyUpdate() ---
        if (hasUpdate)
        {
            sb.AppendLine();
            sb.AppendLine($"    public void ApplyUpdate({entity.Name} entity, Update{entity.Name} dto)");
            sb.AppendLine("    {");

            foreach (var p in updateProps)
            {
                // Optional<T> — only apply when HasValue
                sb.AppendLine($"        if (dto.{p}.HasValue) entity.{p} = dto.{p}.Value!;");
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ---------------------------------------------------------------------------
    // Interface list builder
    // ---------------------------------------------------------------------------

    private static string BuildInterfaceList(EntityMetadata entity, bool hasCreate, bool hasUpdate)
    {
        string n    = entity.Name;
        string resp = $"IResponseMapper<{n}, {n}Response>";
        string cre  = $"ICreateMapper<{n}, Create{n}>";
        string upd  = $"IUpdateMapper<{n}, Update{n}>";
        string crud = $"ICrudMapper<{n}, Create{n}, Update{n}, {n}Response>";

        if (hasCreate && hasUpdate)
            return crud;

        if (!hasCreate && !hasUpdate)
            return resp;

        if (hasCreate)          // update disabled
            return $"{resp}, {cre}";

        return $"{resp}, {upd}"; // create disabled
    }

    // ---------------------------------------------------------------------------
    // Property list helpers
    // ---------------------------------------------------------------------------

    private static List<string> BuildResponseProps(EntityMetadata entity)
    {
        var props = new List<string> { "Id", "CreatedAt", "UpdatedAt" };

        if (entity.ImplementsISoftDeletable)
            props.Add("DeletedAt");

        if (entity.ImplementsIMultiTenant)
            props.Add("TenantId");

        foreach (var p in entity.Properties)
        {
            if (!p.IsSkipResponse)
                props.Add(p.Name);
        }

        return props;
    }

    private static List<string> BuildCreateProps(EntityMetadata entity)
    {
        var props = new List<string>();
        foreach (var p in entity.Properties)
        {
            if (!p.IsProtected)
                props.Add(p.Name);
        }
        return props;
    }

    private static List<string> BuildUpdateProps(EntityMetadata entity)
    {
        var props = new List<string>();
        foreach (var p in entity.Properties)
        {
            if (!p.IsProtected && !p.IsSkipUpdate)
                props.Add(p.Name);
        }
        return props;
    }

    private static List<string> BuildAssignments(List<string> propNames)
    {
        // Simple direct assignment — entity.PropName
        return propNames;
    }
}
```

- [ ] **7.2** Create `tests/CrudKit.SourceGen.Tests/Generators/MapperGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class MapperGeneratorTests
{
    private const string InvoiceEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace Billing.Entities
        {
            [CrudEntity(Table = "Invoices", SoftDelete = true)]
            public class Invoice : IEntity, ISoftDeletable
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }

                public string Number { get; set; } = string.Empty;
                public decimal Amount { get; set; }

                [SkipResponse]
                public string PaymentToken { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void Mapper_ImplementsICrudMapper_WhenAllOpsEnabled()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        // InvoiceEntity has create + update enabled → ICrudMapper
        Assert.Contains("ICrudMapper<Invoice, CreateInvoice, UpdateInvoice, InvoiceResponse>", source);
    }

    [Fact]
    public void Mapper_ImplementsIResponseMapper_WhenReadOnly()
    {
        const string readonlyEntity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
            {
                [CrudEntity(Table = "Summaries", ReadOnly = true)]
                public class Summary : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public decimal Total { get; set; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(readonlyEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "SummaryMapper.g.cs");

        Assert.Contains("IResponseMapper<Summary, SummaryResponse>", source);
        Assert.DoesNotContain("ICrudMapper", source);
        Assert.DoesNotContain("ICreateMapper", source);
        Assert.DoesNotContain("IUpdateMapper", source);
    }

    [Fact]
    public void Mapper_HasMapMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public InvoiceResponse Map(Invoice entity)", source);
    }

    [Fact]
    public void Mapper_HasProjectMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public IQueryable<InvoiceResponse> Project(IQueryable<Invoice> query)", source);
    }

    [Fact]
    public void Mapper_HasFromCreateDtoMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public Invoice FromCreateDto(CreateInvoice dto)", source);
    }

    [Fact]
    public void Mapper_HasApplyUpdateMethod()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("public void ApplyUpdate(Invoice entity, UpdateInvoice dto)", source);
    }

    [Fact]
    public void Mapper_ProjectUsesSelectLambda()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("query.Select(entity => new InvoiceResponse(", source);
    }

    [Fact]
    public void Mapper_ExcludesSkipResponseFields()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.DoesNotContain("PaymentToken", source);
    }

    [Fact]
    public void Mapper_IncludesDeletedAt_WhenSoftDeletable()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(InvoiceEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "InvoiceMapper.g.cs");

        Assert.Contains("entity.DeletedAt", source);
    }

    [Fact]
    public void Mapper_NoFromCreateDto_WhenCreateDisabled()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
            {
                [CrudEntity(Table = "Logs", EnableCreate = false)]
                public class Log : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Message { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "LogMapper.g.cs");

        Assert.DoesNotContain("FromCreateDto", source);
        Assert.DoesNotContain("ICreateMapper", source);
        // Should still have update and response
        Assert.Contains("IUpdateMapper", source);
        Assert.Contains("IResponseMapper", source);
    }

    [Fact]
    public void Mapper_NoApplyUpdate_WhenUpdateDisabled()
    {
        const string entity = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace Billing.Entities
            {
                [CrudEntity(Table = "Immutable", EnableUpdate = false)]
                public class ImmutableLog : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Data { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(entity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ImmutableLogMapper.g.cs");

        Assert.DoesNotContain("ApplyUpdate", source);
        Assert.DoesNotContain("IUpdateMapper", source);
        // Should still have create and response
        Assert.Contains("ICreateMapper", source);
        Assert.Contains("IResponseMapper", source);
    }
}
```

---

## Task 8: EndpointMappingGenerator + HookStubGenerator

Generates `CrudKitEndpoints.g.cs` (`MapAllCrudEndpoints()`) and per-entity hook stubs.

### Steps

- [ ] **8.1** Create `src/CrudKit.SourceGen/Generators/HookStubGenerator.cs`:

```csharp
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates a <c>partial class {Name}Hooks : ICrudHooks&lt;{Name}&gt;</c> stub.
/// The partial class is intentionally empty — developers fill it in.
/// </summary>
internal static class HookStubGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("// Rename this file and remove the partial keyword to customize hooks.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"using {entity.Namespace}.Entities;");
        sb.AppendLine("using CrudKit.Core.Interfaces;");
        sb.AppendLine();
        sb.AppendLine($"namespace {entity.Namespace}.Hooks;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Hook stub for <see cref=\"{entity.Name}\"/>.");
        sb.AppendLine($"/// Override methods from <see cref=\"ICrudHooks{{T}}\"/> as needed.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public partial class {entity.Name}Hooks : ICrudHooks<{entity.Name}>");
        sb.AppendLine("{");
        sb.AppendLine("    // Override BeforeCreate, AfterCreate, BeforeUpdate, AfterUpdate,");
        sb.AppendLine("    // BeforeDelete, AfterDelete, ApplyScope, and ApplyIncludes as needed.");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **8.2** Create `src/CrudKit.SourceGen/Generators/EndpointMappingGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates <c>CrudKitEndpoints.g.cs</c> containing a <c>MapAllCrudEndpoints()</c>
/// extension method on <c>IEndpointRouteBuilder</c>.
/// Each entity uses <c>MapCrudEndpoints</c> (full) or <c>MapReadOnlyEndpoints</c>
/// depending on its operation flags.
/// </summary>
internal static class EndpointMappingGenerator
{
    public static string Generate(IReadOnlyList<EntityMetadata> entities)
    {
        if (entities.Count == 0)
            return string.Empty;

        // Collect distinct namespaces used by entity types
        var namespaces = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (var e in entities)
        {
            namespaces.Add($"{e.Namespace}.Entities");
            namespaces.Add($"{e.Namespace}.Dtos");
            namespaces.Add($"{e.Namespace}.Mappers");
            namespaces.Add($"{e.Namespace}.Hooks");
        }

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using CrudKit.Api;");
        foreach (var ns in namespaces)
            sb.AppendLine($"using {ns};");
        sb.AppendLine();

        // Determine the common namespace (use first entity's namespace root)
        string rootNs = entities[0].Namespace;
        sb.AppendLine($"namespace {rootNs};");
        sb.AppendLine();
        sb.AppendLine("public static class CrudKitEndpoints");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Maps all CrudKit CRUD endpoints discovered by source generation.</summary>");
        sb.AppendLine("    public static IEndpointRouteBuilder MapAllCrudEndpoints(this IEndpointRouteBuilder app)");
        sb.AppendLine("    {");

        foreach (var entity in entities)
        {
            // Determine which mapper overload to call
            if (entity.ReadOnly || (!entity.IsCreateEnabled && !entity.IsUpdateEnabled))
            {
                // Read-only: only GET endpoints
                sb.AppendLine($"        app.MapReadOnlyEndpoints<{entity.Name}, {entity.Name}Response, {entity.Name}Mapper, {entity.Name}Hooks>();");
            }
            else if (entity.IsCreateEnabled && entity.IsUpdateEnabled)
            {
                sb.AppendLine($"        app.MapCrudEndpoints<{entity.Name}, Create{entity.Name}, Update{entity.Name}, {entity.Name}Response, {entity.Name}Mapper, {entity.Name}Hooks>();");
            }
            else if (entity.IsCreateEnabled && !entity.IsUpdateEnabled)
            {
                sb.AppendLine($"        app.MapCreateOnlyEndpoints<{entity.Name}, Create{entity.Name}, {entity.Name}Response, {entity.Name}Mapper, {entity.Name}Hooks>();");
            }
            else
            {
                // EnableUpdate only (no create)
                sb.AppendLine($"        app.MapUpdateOnlyEndpoints<{entity.Name}, Update{entity.Name}, {entity.Name}Response, {entity.Name}Mapper, {entity.Name}Hooks>();");
            }
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **8.3** Create `tests/CrudKit.SourceGen.Tests/Generators/EndpointMappingGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class EndpointMappingGeneratorTests
{
    private const string TwoEntities = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace App.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Catalogs", ReadOnly = true)]
            public class Catalog : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Title { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void EndpointMapping_GeneratesMapAllCrudEndpoints()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapAllCrudEndpoints", source);
        Assert.Contains("IEndpointRouteBuilder", source);
    }

    [Fact]
    public void EndpointMapping_UsesFullCrudOverload_ForFullEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Product, CreateProduct, UpdateProduct,", source);
    }

    [Fact]
    public void EndpointMapping_UsesReadOnlyOverload_ForReadOnlyEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapReadOnlyEndpoints<Catalog,", source);
    }

    [Fact]
    public void HookStub_Generated_PerEntity()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);

        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("ProductHooks"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("CatalogHooks"));
    }

    [Fact]
    public void HookStub_ImplementsICrudHooks()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(TwoEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductHooks.g.cs");

        Assert.Contains("ICrudHooks<Product>", source);
        Assert.Contains("partial class ProductHooks", source);
    }
}
```

---

## Task 9: DI Registration Generator

Generates `CrudKitMappers.g.cs` with an `AddAllCrudMappers()` extension on `IServiceCollection`.

### Steps

- [ ] **9.1** Create `src/CrudKit.SourceGen/Generators/DiRegistrationGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using CrudKit.SourceGen.Models;

namespace CrudKit.SourceGen.Generators;

/// <summary>
/// Generates <c>CrudKitMappers.g.cs</c> containing an <c>AddAllCrudMappers()</c>
/// extension on <c>IServiceCollection</c> that registers all generated mapper classes.
/// </summary>
internal static class DiRegistrationGenerator
{
    public static string Generate(IReadOnlyList<EntityMetadata> entities)
    {
        if (entities.Count == 0)
            return string.Empty;

        var namespaces = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (var e in entities)
        {
            namespaces.Add($"{e.Namespace}.Mappers");
            namespaces.Add($"{e.Namespace}.Dtos");
            namespaces.Add($"{e.Namespace}.Entities");
        }

        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by CrudKit.SourceGen — do not edit manually.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using CrudKit.Core.Interfaces;");
        foreach (var ns in namespaces)
            sb.AppendLine($"using {ns};");
        sb.AppendLine();

        string rootNs = entities[0].Namespace;
        sb.AppendLine($"namespace {rootNs};");
        sb.AppendLine();
        sb.AppendLine("public static class CrudKitMappers");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers all CrudKit-generated mapper implementations as scoped services.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static IServiceCollection AddAllCrudMappers(this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var entity in entities)
        {
            bool hasCreate = entity.IsCreateEnabled;
            bool hasUpdate = entity.IsUpdateEnabled;

            if (hasCreate && hasUpdate)
            {
                // Full CRUD: register as ICrudMapper, then forward individual interfaces
                sb.AppendLine($"        services.AddScoped<ICrudMapper<{entity.Name}, Create{entity.Name}, Update{entity.Name}, {entity.Name}Response>, {entity.Name}Mapper>();");
                sb.AppendLine($"        services.AddScoped<IResponseMapper<{entity.Name}, {entity.Name}Response>>(sp =>");
                sb.AppendLine($"            sp.GetRequiredService<ICrudMapper<{entity.Name}, Create{entity.Name}, Update{entity.Name}, {entity.Name}Response>>());");
                sb.AppendLine($"        services.AddScoped<ICreateMapper<{entity.Name}, Create{entity.Name}>>(sp =>");
                sb.AppendLine($"            sp.GetRequiredService<ICrudMapper<{entity.Name}, Create{entity.Name}, Update{entity.Name}, {entity.Name}Response>>());");
                sb.AppendLine($"        services.AddScoped<IUpdateMapper<{entity.Name}, Update{entity.Name}>>(sp =>");
                sb.AppendLine($"            sp.GetRequiredService<ICrudMapper<{entity.Name}, Create{entity.Name}, Update{entity.Name}, {entity.Name}Response>>());");
            }
            else if (!hasCreate && !hasUpdate)
            {
                // ReadOnly: only IResponseMapper
                sb.AppendLine($"        services.AddScoped<IResponseMapper<{entity.Name}, {entity.Name}Response>, {entity.Name}Mapper>();");
            }
            else if (hasCreate)
            {
                // Update disabled: IResponseMapper + ICreateMapper
                sb.AppendLine($"        services.AddScoped<IResponseMapper<{entity.Name}, {entity.Name}Response>, {entity.Name}Mapper>();");
                sb.AppendLine($"        services.AddScoped<ICreateMapper<{entity.Name}, Create{entity.Name}>>(sp =>");
                sb.AppendLine($"            (ICreateMapper<{entity.Name}, Create{entity.Name}>)sp.GetRequiredService<IResponseMapper<{entity.Name}, {entity.Name}Response>>());");
            }
            else
            {
                // Create disabled: IResponseMapper + IUpdateMapper
                sb.AppendLine($"        services.AddScoped<IResponseMapper<{entity.Name}, {entity.Name}Response>, {entity.Name}Mapper>();");
                sb.AppendLine($"        services.AddScoped<IUpdateMapper<{entity.Name}, Update{entity.Name}>>(sp =>");
                sb.AppendLine($"            (IUpdateMapper<{entity.Name}, Update{entity.Name}>)sp.GetRequiredService<IResponseMapper<{entity.Name}, {entity.Name}Response>>());");
            }
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

- [ ] **9.2** Create `tests/CrudKit.SourceGen.Tests/Generators/DiRegistrationGeneratorTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

public class DiRegistrationGeneratorTests
{
    private const string ThreeEntities = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;

        namespace App.Entities
        {
            [CrudEntity(Table = "Products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Categories")]
            public class Category : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Label { get; set; } = string.Empty;
            }

            [CrudEntity(Table = "Brands")]
            public class Brand : IEntity
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public string Name { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void DiRegistration_GeneratesAddAllCrudMappers()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("AddAllCrudMappers", source);
        Assert.Contains("IServiceCollection", source);
    }

    [Fact]
    public void DiRegistration_RegistersICrudMapper_ForFullCrudEntities()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Full CRUD entities are registered as ICrudMapper
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>, ProductMapper", source);
        Assert.Contains("ICrudMapper<Category, CreateCategory, UpdateCategory, CategoryResponse>, CategoryMapper", source);
        Assert.Contains("ICrudMapper<Brand, CreateBrand, UpdateBrand, BrandResponse>, BrandMapper", source);
    }

    [Fact]
    public void DiRegistration_ForwardsIndividualInterfaces_ForFullCrudEntities()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Individual interface forwarding registrations must be present
        Assert.Contains("IResponseMapper<Product, ProductResponse>", source);
        Assert.Contains("ICreateMapper<Product, CreateProduct>", source);
        Assert.Contains("IUpdateMapper<Product, UpdateProduct>", source);
    }

    [Fact]
    public void DiRegistration_RegistersIResponseMapperOnly_ForReadOnlyEntity()
    {
        const string readonlyEntities = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System;

            namespace App.Entities
            {
                [CrudEntity(Table = "Reports", ReadOnly = true)]
                public class Report : IEntity
                {
                    public string Id { get; set; } = string.Empty;
                    public DateTime CreatedAt { get; set; }
                    public DateTime UpdatedAt { get; set; }
                    public string Title { get; set; } = string.Empty;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(readonlyEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("IResponseMapper<Report, ReportResponse>, ReportMapper", source);
        Assert.DoesNotContain("ICrudMapper", source);
        Assert.DoesNotContain("ICreateMapper", source);
        Assert.DoesNotContain("IUpdateMapper", source);
    }

    [Fact]
    public void DiRegistration_UsesAddScoped()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Each full-CRUD entity produces 4 AddScoped calls (ICrudMapper + 3 forwarding)
        // Three entities = 12 total AddScoped calls
        var scopedCount = source.Split(new[] { "AddScoped" }, System.StringSplitOptions.None).Length - 1;
        Assert.Equal(12, scopedCount);
    }

    [Fact]
    public void DiRegistration_ImportsRequiredNamespaces()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(ThreeEntities);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        Assert.Contains("using Microsoft.Extensions.DependencyInjection;", source);
        Assert.Contains("using CrudKit.Core.Interfaces;", source);
    }
}
```

---

## Task 10: CrudKitSourceGenerator (main wiring)

Wire the IIncrementalGenerator entry point: `ForAttributeWithMetadataName` → `EntityParser` → all sub-generators. Replace the Task 1 placeholder.

### Steps

- [ ] **10.1** Replace `src/CrudKit.SourceGen/CrudKitSourceGenerator.cs` with the full implementation:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Generators;
using CrudKit.SourceGen.Models;
using CrudKit.SourceGen.Parsing;

namespace CrudKit.SourceGen;

/// <summary>
/// Roslyn incremental source generator for CrudKit.
/// Scans for classes decorated with [CrudEntity] and generates:
///   - CreateDto, UpdateDto, ResponseDto
///   - Mapper (ICrudMapper / IResponseMapper implementation)
///   - Hook stub (partial class implementing ICrudHooks)
///   - Collective endpoint mapping and DI registration
/// </summary>
[Generator]
public sealed class CrudKitSourceGenerator : IIncrementalGenerator
{
    private const string CrudEntityAttributeFqn = "CrudKit.Core.Attributes.CrudEntityAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Find all classes decorated with [CrudEntity]
        var entitySymbols = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CrudEntityAttributeFqn,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return (Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                            Location: ctx.TargetNode.GetLocation());
                })
            .WithTrackingName("CrudEntityClasses");

        // 2. Validate and emit diagnostics
        context.RegisterSourceOutput(entitySymbols, static (spc, item) =>
        {
            var (symbol, location) = item;

            // CRUD010 — check for explicit empty Table name before parsing
            var attr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CrudEntityAttributeFqn);

            if (attr != null)
            {
                var tableArg = attr.NamedArguments
                    .FirstOrDefault(kv => kv.Key == "Table");

                // TypedConstant default is empty — only report if explicitly set to empty string
                if (tableArg.Key == "Table"
                    && tableArg.Value.Value is string tableVal
                    && tableVal.Length == 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.EmptyTableName,
                        location,
                        symbol.Name));
                    return; // Cannot generate safely without a table name
                }
            }

            var metadata = EntityParser.Parse(symbol);
            if (metadata is null)
                return;

            // Emit CRUD001/002/003 diagnostics
            var diagnostics = EntityValidator.Validate(metadata, location);
            foreach (var diag in diagnostics)
                spc.ReportDiagnostic(diag);

            // Do not generate code if IEntity is missing (CRUD001)
            if (!metadata.ImplementsIEntity)
                return;

            // Per-entity files
            EmitPerEntityFiles(spc, metadata);
        });

        // 3. Collect all valid entities for collective generators
        var allEntities = entitySymbols
            .Where(static item => EntityParser.Parse(item.Symbol) != null)
            .Select(static (item, _) => EntityParser.Parse(item.Symbol)!)
            .Where(static m => m.ImplementsIEntity)
            .Collect();

        // 4. Emit collective files
        context.RegisterSourceOutput(allEntities, static (spc, entities) =>
        {
            if (entities.IsEmpty)
                return;

            var list = entities.ToList();

            // CrudKitEndpoints.g.cs
            var endpointsSource = EndpointMappingGenerator.Generate(list);
            if (!string.IsNullOrEmpty(endpointsSource))
                spc.AddSource("CrudKitEndpoints.g.cs", endpointsSource);

            // CrudKitMappers.g.cs
            var mappersSource = DiRegistrationGenerator.Generate(list);
            if (!string.IsNullOrEmpty(mappersSource))
                spc.AddSource("CrudKitMappers.g.cs", mappersSource);
        });
    }

    // ---------------------------------------------------------------------------
    // Per-entity file emission
    // ---------------------------------------------------------------------------

    private static void EmitPerEntityFiles(SourceProductionContext spc, EntityMetadata entity)
    {
        // CreateDto — null when disabled
        var createDtoSource = CreateDtoGenerator.Generate(entity);
        if (createDtoSource != null)
            spc.AddSource($"{entity.Name}CreateDto.g.cs", createDtoSource);

        // UpdateDto — null when disabled
        var updateDtoSource = UpdateDtoGenerator.Generate(entity);
        if (updateDtoSource != null)
            spc.AddSource($"{entity.Name}UpdateDto.g.cs", updateDtoSource);

        // ResponseDto — always emitted
        var responseDtoSource = ResponseDtoGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}ResponseDto.g.cs", responseDtoSource);

        // Mapper — always emitted
        var mapperSource = MapperGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}Mapper.g.cs", mapperSource);

        // Hook stub — always emitted
        var hookStubSource = HookStubGenerator.Generate(entity);
        spc.AddSource($"{entity.Name}Hooks.g.cs", hookStubSource);
    }
}
```

- [ ] **10.2** Create `tests/CrudKit.SourceGen.Tests/Generators/IntegrationTests.cs`:

```csharp
using CrudKit.SourceGen.Tests.Helpers;
using Xunit;

namespace CrudKit.SourceGen.Tests.Generators;

/// <summary>
/// End-to-end: one fully-featured entity → verifies all expected files are generated.
/// </summary>
public class IntegrationTests
{
    private const string FullEntity = """
        using CrudKit.Core.Attributes;
        using CrudKit.Core.Interfaces;
        using System;
        using System.ComponentModel.DataAnnotations;

        namespace Acme.Domain.Entities
        {
            [CrudEntity(
                Table = "Products",
                SoftDelete = true,
                Audit = true,
                MultiTenant = true,
                EnableBulkUpdate = true)]
            public class Product : IEntity, ISoftDeletable, IMultiTenant
            {
                public string Id { get; set; } = string.Empty;
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
                public string TenantId { get; set; } = string.Empty;

                [Required]
                [MaxLength(200)]
                public string Name { get; set; } = string.Empty;

                [Range(0.01, 999999.99)]
                public decimal Price { get; set; }

                public string? Description { get; set; }

                [SkipUpdate]
                public string Sku { get; set; } = string.Empty;

                [SkipResponse]
                public string InternalTag { get; set; } = string.Empty;

                [Protected]
                public string CreatedById { get; set; } = string.Empty;

                [Searchable]
                public string Tags { get; set; } = string.Empty;
            }
        }
        """;

    [Fact]
    public void FullEntity_GeneratesAllExpectedFiles()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var fileNames = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();

        Assert.Contains("ProductCreateDto.g.cs", fileNames);
        Assert.Contains("ProductUpdateDto.g.cs", fileNames);
        Assert.Contains("ProductResponseDto.g.cs", fileNames);
        Assert.Contains("ProductMapper.g.cs", fileNames);
        Assert.Contains("ProductHooks.g.cs", fileNames);
        Assert.Contains("CrudKitEndpoints.g.cs", fileNames);
        Assert.Contains("CrudKitMappers.g.cs", fileNames);
    }

    [Fact]
    public void FullEntity_NoCrudDiagnosticsEmitted()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);

        var crudDiags = result.Diagnostics.Where(d => d.Id.StartsWith("CRUD")).ToList();
        Assert.Empty(crudDiags);
    }

    [Fact]
    public void FullEntity_CreateDto_HasCorrectShape()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductCreateDto.g.cs");

        // Required props
        Assert.Contains("[Required]", source);
        Assert.Contains("[MaxLength(200)]", source);
        Assert.Contains("string Name", source);
        Assert.Contains("[Range(0.01, 999999.99)]", source);
        Assert.Contains("decimal Price", source);

        // System fields excluded
        Assert.DoesNotContain("CreatedAt", source);
        Assert.DoesNotContain("TenantId", source);

        // Protected excluded
        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void FullEntity_UpdateDto_WrapsInOptional()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductUpdateDto.g.cs");

        Assert.Contains("Optional<string> Name", source);
        Assert.Contains("Optional<decimal> Price", source);

        // SkipUpdate and Protected excluded
        Assert.DoesNotContain("Sku", source);
        Assert.DoesNotContain("CreatedById", source);
    }

    [Fact]
    public void FullEntity_ResponseDto_ExcludesSkipResponse()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductResponseDto.g.cs");

        Assert.DoesNotContain("InternalTag", source);
        Assert.Contains("DateTime? DeletedAt", source);
        Assert.Contains("string TenantId", source);
    }

    [Fact]
    public void FullEntity_Mapper_IsComplete()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "ProductMapper.g.cs");

        // Full CRUD entity → ICrudMapper
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>", source);
        Assert.Contains("public ProductResponse Map(Product entity)", source);
        Assert.Contains("public IQueryable<ProductResponse> Project(IQueryable<Product> query)", source);
        Assert.Contains("public Product FromCreateDto(CreateProduct dto)", source);
        Assert.Contains("public void ApplyUpdate(Product entity, UpdateProduct dto)", source);
    }

    [Fact]
    public void FullEntity_EndpointMapping_UsesCrudOverload()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitEndpoints.g.cs");

        Assert.Contains("MapCrudEndpoints<Product, CreateProduct, UpdateProduct,", source);
    }

    [Fact]
    public void FullEntity_DiRegistration_RegistersMapper()
    {
        var result = GeneratorTestHelper.RunGenerator<CrudKitSourceGenerator>(FullEntity);
        var source = GeneratorTestHelper.GetGeneratedSource(result, "CrudKitMappers.g.cs");

        // Full CRUD entity → registered as ICrudMapper with individual interface forwarding
        Assert.Contains("ICrudMapper<Product, CreateProduct, UpdateProduct, ProductResponse>, ProductMapper", source);
        Assert.Contains("IResponseMapper<Product, ProductResponse>", source);
        Assert.Contains("ICreateMapper<Product, CreateProduct>", source);
        Assert.Contains("IUpdateMapper<Product, UpdateProduct>", source);
    }
}
```

---

## Task 11: CrudEntityAttribute Operation Control (Core change)

Add `ReadOnly`, `EnableCreate`, `EnableUpdate`, `EnableDelete`, `EnableBulkDelete` to `CrudKit.Core`'s `CrudEntityAttribute`, plus computed helpers.

### Steps

- [ ] **11.1** Update `src/CrudKit.Core/Attributes/CrudEntityAttribute.cs`:

```csharp
namespace CrudKit.Core.Attributes;

/// <summary>
/// Configures entity behavior: table mapping, soft delete, audit logging, multi-tenancy,
/// workflow, bulk operations, and fine-grained operation control.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CrudEntityAttribute : Attribute
{
    public string Table { get; set; } = string.Empty;
    public bool SoftDelete { get; set; }
    public bool Audit { get; set; }
    public bool MultiTenant { get; set; }
    public string? Workflow { get; set; }
    public string[]? WorkflowProtected { get; set; }
    public string? NumberingPrefix { get; set; }
    public bool NumberingYearlyReset { get; set; } = true;
    public bool EnableBulkUpdate { get; set; }
    public int BulkLimit { get; set; } = 0; // 0 = use global default from CrudKitApiOptions

    /// <summary>
    /// Property name on the entity that holds the owner user ID.
    /// Used with PermScope.Own to filter entities by owner.
    /// </summary>
    public string? OwnerField { get; set; }

    // ---------------------------------------------------------------------------
    // Operation control
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When true, disables all mutating endpoints (Create, Update, Delete).
    /// Overrides EnableCreate, EnableUpdate, and EnableDelete.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>Enables the POST (create) endpoint. Ignored when ReadOnly=true.</summary>
    public bool EnableCreate { get; set; } = true;

    /// <summary>Enables the PATCH/PUT (update) endpoint. Ignored when ReadOnly=true.</summary>
    public bool EnableUpdate { get; set; } = true;

    /// <summary>Enables the DELETE endpoint. Ignored when ReadOnly=true.</summary>
    public bool EnableDelete { get; set; } = true;

    /// <summary>Enables the bulk DELETE endpoint. Ignored when ReadOnly=true.</summary>
    public bool EnableBulkDelete { get; set; }

    // ---------------------------------------------------------------------------
    // Computed helpers (read-only, not settable via attribute syntax)
    // ---------------------------------------------------------------------------

    /// <summary>True when both ReadOnly is false and EnableCreate is true.</summary>
    public bool IsCreateEnabled => !ReadOnly && EnableCreate;

    /// <summary>True when both ReadOnly is false and EnableUpdate is true.</summary>
    public bool IsUpdateEnabled => !ReadOnly && EnableUpdate;

    /// <summary>True when both ReadOnly is false and EnableDelete is true.</summary>
    public bool IsDeleteEnabled => !ReadOnly && EnableDelete;
}
```

- [ ] **11.2** Add `tests/CrudKit.Core.Tests/Attributes/CrudEntityAttributeTests.cs`:

```csharp
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class CrudEntityAttributeTests
{
    [Fact]
    public void DefaultAttribute_AllOperationsEnabled()
    {
        var attr = new CrudEntityAttribute();

        Assert.True(attr.IsCreateEnabled);
        Assert.True(attr.IsUpdateEnabled);
        Assert.True(attr.IsDeleteEnabled);
    }

    [Fact]
    public void ReadOnly_DisablesAllMutatingOperations()
    {
        var attr = new CrudEntityAttribute { ReadOnly = true };

        Assert.False(attr.IsCreateEnabled);
        Assert.False(attr.IsUpdateEnabled);
        Assert.False(attr.IsDeleteEnabled);
    }

    [Fact]
    public void ReadOnly_OverridesIndividualEnableFlags()
    {
        var attr = new CrudEntityAttribute
        {
            ReadOnly = true,
            EnableCreate = true,
            EnableUpdate = true,
            EnableDelete = true
        };

        // ReadOnly takes precedence over individual flags
        Assert.False(attr.IsCreateEnabled);
        Assert.False(attr.IsUpdateEnabled);
        Assert.False(attr.IsDeleteEnabled);
    }

    [Fact]
    public void EnableCreate_False_DisablesCreate_Only()
    {
        var attr = new CrudEntityAttribute { EnableCreate = false };

        Assert.False(attr.IsCreateEnabled);
        Assert.True(attr.IsUpdateEnabled);
        Assert.True(attr.IsDeleteEnabled);
    }

    [Fact]
    public void EnableUpdate_False_DisablesUpdate_Only()
    {
        var attr = new CrudEntityAttribute { EnableUpdate = false };

        Assert.True(attr.IsCreateEnabled);
        Assert.False(attr.IsUpdateEnabled);
        Assert.True(attr.IsDeleteEnabled);
    }

    [Fact]
    public void EnableDelete_False_DisablesDelete_Only()
    {
        var attr = new CrudEntityAttribute { EnableDelete = false };

        Assert.True(attr.IsCreateEnabled);
        Assert.True(attr.IsUpdateEnabled);
        Assert.False(attr.IsDeleteEnabled);
    }

    [Fact]
    public void EnableBulkDelete_DefaultsFalse()
    {
        var attr = new CrudEntityAttribute();

        Assert.False(attr.EnableBulkDelete);
    }
}
```

---

## Implementation Order Summary

| Task | Description | Dependencies |
|------|-------------|--------------|
| 11 | Add operation-control props to CrudEntityAttribute (Core) | None |
| 1  | Project scaffold + test helper | None |
| 2  | EntityMetadata + EntityParser | Task 1 |
| 3  | DiagnosticDescriptors + EntityValidator | Task 2 |
| 4  | CreateDtoGenerator | Task 2 |
| 5  | UpdateDtoGenerator | Task 2 |
| 6  | ResponseDtoGenerator | Task 2 |
| 7  | MapperGenerator | Task 6 |
| 8  | EndpointMappingGenerator + HookStubGenerator | Tasks 4, 5 |
| 9  | DiRegistrationGenerator | Task 6 |
| 10 | CrudKitSourceGenerator (wiring) | All above |

**Recommended execution:** Task 11 first (Core change, no dependencies), then Tasks 1→2→3 together, then 4/5/6/7/8/9 in parallel, then Task 10 last.

---

## Key Design Decisions

- **netstandard2.0 target** — required for Roslyn analyzers/generators; all code uses C# language features available with `LangVersion latest` but API calls limited to netstandard2.0 surface.
- **PrivateAssets=all on Roslyn packages** — prevents analyzer packages from flowing into consuming projects as runtime dependencies.
- **ForAttributeWithMetadataName** — most efficient Roslyn incremental API for attribute-filtered scanning; avoids re-running on unrelated syntax changes.
- **Immutable EntityMetadata/PropertyMetadata** — incremental generators require equatable, side-effect-free models for caching; all fields are set via constructor only.
- **Per-entity hint names** — `{Name}CreateDto.g.cs` pattern allows the IDE to display generated files in a predictable location under the project's Analyzers node.
- **Collective generators use `.Collect()`** — endpoint mapping and DI registration need all entities together; `.Collect()` is the standard incremental API for this pattern.
- **Diagnostics before generation** — CRUD001 short-circuits generation for the affected entity to prevent cascade compile errors from badly-typed generated code.
