using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.Core.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void PhaseMarkerIdentifiesTheExecutionCoreMilestone()
    {
        AssemblyMarker.Phase.Should().Be("Phase2");
    }
}

