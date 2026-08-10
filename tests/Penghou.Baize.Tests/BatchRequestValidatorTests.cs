using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class BatchRequestValidatorTests
{
    [Fact]
    public void ValidateItems_RejectsDuplicateIds()
    {
        var request = new LlmRequest([new LlmMessage("user", "hello")]);

        var action = () => BatchRequestValidator.ValidateItems(
            [new BaizeBatchItem("same", request), new BaizeBatchItem("same", request)],
            "test");

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate batch request id 'same'*");
    }

    [Fact]
    public void ValidateHandle_RejectsWrongProvider()
    {
        var action = () => BatchRequestValidator.ValidateHandle(
            new ProviderBatchHandle("other", "batch-1"),
            "expected");

        action.Should().Throw<ArgumentException>()
            .WithMessage("*belongs to provider 'other'*");
    }
}
