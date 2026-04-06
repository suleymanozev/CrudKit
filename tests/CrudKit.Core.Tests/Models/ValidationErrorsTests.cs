using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class ValidationErrorsTests
{
    [Fact]
    public void NewInstance_ShouldBeEmpty()
    {
        var errors = new ValidationErrors();
        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Add_ShouldMakeNonEmpty()
    {
        var errors = new ValidationErrors();
        errors.Add("email", "required", "Email zorunludur");
        Assert.False(errors.IsEmpty);
        Assert.Single(errors.Errors);
    }

    [Fact]
    public void ThrowIfInvalid_ShouldNotThrowWhenEmpty()
    {
        var errors = new ValidationErrors();
        errors.ThrowIfInvalid();  // exception fırlatmamalı
    }

    [Fact]
    public void ThrowIfInvalid_ShouldThrowWhenNotEmpty()
    {
        var errors = new ValidationErrors();
        errors.Add("name", "required", "İsim zorunludur");
        var ex = Assert.Throws<AppError>(() => errors.ThrowIfInvalid());
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void MultipleErrors_ShouldAccumulate()
    {
        var errors = new ValidationErrors();
        errors.Add("email", "required", "Email zorunludur");
        errors.Add("email", "email", "Geçerli email giriniz");
        errors.Add("age", "min", "Yaş 0'dan büyük olmalı");
        Assert.Equal(3, errors.Errors.Count);
    }

    // ---- ToDictionary ----

    [Fact]
    public void ToDictionary_GroupsByField()
    {
        var errors = new ValidationErrors();
        errors.Add("Name", "REQUIRED", "Name is required");
        errors.Add("Name", "LENGTH", "Name too short");
        errors.Add("Email", "FORMAT", "Invalid email");

        var dict = errors.ToDictionary();

        Assert.Equal(2, dict.Count);
        Assert.Equal(2, dict["Name"].Length);
        Assert.Single(dict["Email"]);
    }

    [Fact]
    public void ToDictionary_EmptyErrors_ReturnsEmptyDictionary()
    {
        var errors = new ValidationErrors();

        var dict = errors.ToDictionary();

        Assert.Empty(dict);
    }

    [Fact]
    public void ToDictionary_SingleField_SingleError()
    {
        var errors = new ValidationErrors();
        errors.Add("Age", "MIN", "Age must be positive");

        var dict = errors.ToDictionary();

        Assert.Single(dict);
        Assert.Single(dict["Age"]);
        Assert.Equal("Age must be positive", dict["Age"][0]);
    }

    // ---- ThrowIfInvalid edge cases ----

    [Fact]
    public void ThrowIfInvalid_ThrowsWhenErrors()
    {
        var errors = new ValidationErrors();
        errors.Add("Name", "REQUIRED", "Required");

        Assert.Throws<AppError>(() => errors.ThrowIfInvalid());
    }

    [Fact]
    public void ThrowIfInvalid_DoesNotThrowWhenEmpty()
    {
        var errors = new ValidationErrors();

        // Should not throw
        errors.ThrowIfInvalid();
    }

    [Fact]
    public void ThrowIfInvalid_AppError_HasValidationStatusCode()
    {
        var errors = new ValidationErrors();
        errors.Add("X", "CODE", "msg");

        var ex = Assert.Throws<AppError>(() => errors.ThrowIfInvalid());

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }
}
