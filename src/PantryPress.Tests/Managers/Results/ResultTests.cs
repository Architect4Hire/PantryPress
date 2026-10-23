using PantryPress.Domain.Managers.Results;

namespace PantryPress.Tests.Managers.Results;

// PP1.2b: facades return results for expected failures and throw only for bugs (backend.md).
public sealed class ResultTests
{
    private static readonly Error Refused = new(ErrorKind.Rejected, "refused", "No.");

    [Fact]
    public void Success_has_no_error()
    {
        Assert.True(Result.Success.IsSuccess);
        Assert.Null(Result.Success.Error);
        Assert.Equal(42, Result<int>.Success(42).Value);
    }

    [Fact]
    public void An_error_converts_to_a_failure()
    {
        Result result = Refused;
        Result<int> valued = Refused;

        Assert.False(result.IsSuccess);
        Assert.Same(Refused, result.Error);
        Assert.Same(Refused, valued.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_bug()
    {
        Result<string> result = Refused;

        var error = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("refused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_errors_carry_fields()
    {
        var error = Error.Validation(new Dictionary<string, string[]> { ["email"] = ["Enter your email address."] });

        Assert.Equal(ErrorKind.Validation, error.Kind);
        Assert.Equal("validation-failed", error.Code);
        Assert.Equal(["Enter your email address."], error.Fields["email"]);
    }
}
