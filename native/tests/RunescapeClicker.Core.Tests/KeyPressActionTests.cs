using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.Core.Tests;

public sealed class KeyPressActionTests
{
    [Fact]
    public void ConstructorStoresNormalizedWindowsKeyMetadata()
    {
        var action = new KeyPressAction(0x20, 0x39, false, "Space");

        action.VirtualKey.Should().Be(0x20);
        action.ScanCode.Should().Be(0x39);
        action.IsExtendedKey.Should().BeFalse();
        action.DisplayLabel.Should().Be("Space");
    }

    [Fact]
    public void ConstructorRejectsMissingVirtualKeyAndScanCode()
    {
        var act = () => new KeyPressAction(0, 0, false, "Unset");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ConstructorRejectsBlankDisplayLabel()
    {
        var act = () => new KeyPressAction(0x41, 0x1E, false, "   ");

        act.Should().Throw<ArgumentException>();
    }
}
