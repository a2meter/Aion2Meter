using A2Meter.Dps;
using Xunit;

namespace A2Meter.Tests;

public sealed class JobMappingTests
{
    [Theory]
    [InlineData(37)]
    [InlineData(38)]
    [InlineData(39)]
    [InlineData(40)]
    public void BrawlerFineGrainedCodesMapToBrawler(int jobCode)
    {
        Assert.Equal(8, JobMapping.GameToUiIndex(jobCode));
        Assert.Equal("\uAD8C\uC131", JobMapping.GameToJobName(jobCode));
    }

    [Fact]
    public void BrawlerUiIndexMapsToBrawlerName()
        => Assert.Equal("\uAD8C\uC131", JobMapping.UiToJobName(8));
}
