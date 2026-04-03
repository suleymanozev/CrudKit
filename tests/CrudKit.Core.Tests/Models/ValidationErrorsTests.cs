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
}
