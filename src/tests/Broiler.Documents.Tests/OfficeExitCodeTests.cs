using Broiler.Documents.Office;

namespace Broiler.Documents.Tests;

public sealed class OfficeExitCodeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(69, 69)] // The shell wrapper remaps its provisioning sentinel.
    [InlineData(98, 98)]
    [InlineData(99, 98)]
    [InlineData(218, 98)] // The affected GitHub run's failure count.
    [InlineData(int.MaxValue, 98)]
    public void Comparison_failures_do_not_report_a_harness_failure(int failures, int expected)
    {
        Assert.Equal(expected, OfficeExitCodes.ForFailures(failures));
    }
}
