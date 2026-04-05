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
        // RequiredAttribute, MaxLengthAttribute, RangeAttribute live in
        // System.ComponentModel.Annotations in modern .NET runtimes.
        yield return MetadataReference.CreateFromFile(
            typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location);
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
            public interface IEntity<TKey> where TKey : notnull
            {
                TKey Id { get; set; }
            }

            public interface IEntity : IEntity<Guid> { }

            public interface IAuditableEntity<TKey> : IEntity<TKey> where TKey : notnull
            {
                DateTime CreatedAt { get; set; }
                DateTime UpdatedAt { get; set; }
            }

            public interface IAuditableEntity : IAuditableEntity<Guid>, IEntity { }

            public interface ISoftDeletable
            {
                DateTime? DeletedAt { get; set; }
            }

            public interface IMultiTenant
            {
                string TenantId { get; set; }
            }

            public interface IResponseMapper<TEntity, TResponse>
                where TEntity : class, IAuditableEntity
                where TResponse : class
            {
                TResponse Map(TEntity entity);
                IQueryable<TResponse> Project(IQueryable<TEntity> query);
            }

            public interface ICreateMapper<TEntity, TCreate>
                where TEntity : class, IAuditableEntity
                where TCreate : class
            {
                TEntity FromCreateDto(TCreate dto);
            }

            public interface IUpdateMapper<TEntity, TUpdate>
                where TEntity : class, IAuditableEntity
                where TUpdate : class
            {
                void ApplyUpdate(TEntity entity, TUpdate dto);
            }

            public interface ICrudMapper<TEntity, TCreate, TUpdate, TResponse>
                : IResponseMapper<TEntity, TResponse>,
                  ICreateMapper<TEntity, TCreate>,
                  IUpdateMapper<TEntity, TUpdate>
                where TEntity : class, IAuditableEntity
                where TCreate : class
                where TUpdate : class
                where TResponse : class
            {
            }

            public interface ICrudHooks<T> where T : class, IAuditableEntity { }
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
