using Azure;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.UnitTests.Images;

/// <summary>Edge case "storage unreachable during upload": which SDK failures count as an outage (503) and which are defects.</summary>
public sealed class StorageFailureTranslatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(503)]
    public void RequestFailed_WithStatus0Or5xx_IsUnavailable(int status) =>
        StorageFailureTranslator.IsUnavailable(new RequestFailedException(status, "x"), CancellationToken.None).Should().BeTrue();

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(412)]
    public void RequestFailed_With4xx_IsNotUnavailable(int status) =>
        StorageFailureTranslator.IsUnavailable(new RequestFailedException(status, "x"), CancellationToken.None).Should().BeFalse();

    [Fact]
    public void TransportFailuresAndTimeouts_AreUnavailable()
    {
        StorageFailureTranslator.IsUnavailable(new HttpRequestException("down"), CancellationToken.None).Should().BeTrue();
        StorageFailureTranslator.IsUnavailable(new TimeoutException(), CancellationToken.None).Should().BeTrue();
        StorageFailureTranslator.IsUnavailable(new TaskCanceledException(), CancellationToken.None).Should().BeTrue();
    }

    [Fact]
    public void ACancellationRequestedByTheCaller_IsNotAnOutage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        StorageFailureTranslator.IsUnavailable(new OperationCanceledException(cts.Token), cts.Token).Should().BeFalse();
    }

    [Fact]
    public void AProgrammingError_IsNotUnavailable() =>
        StorageFailureTranslator.IsUnavailable(new InvalidOperationException(), CancellationToken.None).Should().BeFalse();

    [Fact]
    public void ToUnavailable_KeepsTheSdkExceptionAsInnerOnly()
    {
        var inner = new HttpRequestException("SDK-DETAIL");

        var result = StorageFailureTranslator.ToUnavailable(inner);

        result.InnerException.Should().BeSameAs(inner);
        result.Message.Should().NotContain("SDK-DETAIL");
    }
}
