using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.Core.Tests;

public sealed class RunRequestTests
{
    [Fact]
    public void ConstructorRejectsNullActionEntries()
    {
        var act = () => new RunRequest(
            [new DelayAction(TimeSpan.FromMilliseconds(10)), null!],
            StopCondition.HotkeyOnly,
            ExecutionProfile.Default);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("actions");
    }
}
