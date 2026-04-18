using Aexon.Core.Providers;

namespace Aexon.Core.Tests.Providers;

public sealed class ModelPricingTests
{
    [Fact]
    public void EstimateCost_ZeroTokens_ReturnsZero()
    {
        var pricing = new ModelPricing(3m, 15m, 0.3m, 3.75m);

        Assert.Equal(0m, pricing.EstimateCostUsd(0, 0, 0, 0));
    }

    [Fact]
    public void EstimateCost_SumsAllFourBuckets()
    {
        var pricing = new ModelPricing(3m, 15m, 0.3m, 3.75m);

        var cost = pricing.EstimateCostUsd(1_000_000, 1_000_000, 1_000_000, 1_000_000);

        Assert.Equal(3m + 15m + 0.3m + 3.75m, cost);
    }

    [Fact]
    public void Unknown_ZeroRates()
    {
        var cost = ModelPricing.Unknown.EstimateCostUsd(1_000_000, 1_000_000);

        Assert.Equal(0m, cost);
    }
}
