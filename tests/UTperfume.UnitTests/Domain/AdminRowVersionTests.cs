using UTperfume.Application.Common;

namespace UTperfume.UnitTests.Domain;

public sealed class AdminRowVersionTests
{
    [Fact]
    public void BR_A_10_round_trips_a_rowversion_as_base64()
    {
        byte[] version = [0, 0, 0, 0, 0, 0, 0x1F, 0xA0];

        var text = AdminRowVersion.Encode(version);
        var decoded = AdminRowVersion.Decode(text);

        decoded.IsSuccess.Should().BeTrue();
        decoded.Value.Should().Equal(version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("%%%")]
    public void BR_A_10_missing_or_malformed_values_are_a_validation_error_on_rowVersion(string? value)
    {
        var result = AdminRowVersion.Decode(value);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Details.Should().ContainKey("rowVersion");
    }

    [Fact]
    public void Conflict_and_unprocessable_results_carry_their_code()
    {
        Result<int>.Conflict(AdminErrorCodes.DuplicateSlug).Error.Should()
            .Be(new Error(ErrorKind.Conflict, Code: "duplicate_slug"));
        Result<int>.Unprocessable(AdminErrorCodes.InUse).Error!.Code.Should().Be("in_use");
    }
}
