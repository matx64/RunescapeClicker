using FluentAssertions;
using RunescapeClicker.Core;
namespace RunescapeClicker.Automation.Windows.Tests;

public sealed class WindowsInputAdapterTests
{
    [Fact]
    public async Task GetCursorPositionAsyncReturnsTheWin32Location()
    {
        var interop = new FakeWin32Interop
        {
            CursorPosition = new ScreenPoint(-250, 512),
        };
        var adapter = new WindowsInputAdapter(interop);

        var result = await adapter.GetCursorPositionAsync(CancellationToken.None);

        result.Should().Be(CursorLocationResult.Success(new ScreenPoint(-250, 512)));
    }

    [Fact]
    public async Task GetCursorPositionAsyncSurfacesInteropFailure()
    {
        var interop = new FakeWin32Interop
        {
            CursorResult = CursorLocationResult.Failure("cursor unavailable"),
        };
        var adapter = new WindowsInputAdapter(interop);

        var result = await adapter.GetCursorPositionAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Be("cursor unavailable");
        result.FailureKind.Should().Be(InputFailureKind.CursorReadUnavailable);
    }

    [Fact]
    public async Task MoveMouseAsyncDelegatesToTheInteropLayer()
    {
        var interop = new FakeWin32Interop();
        var adapter = new WindowsInputAdapter(interop);

        var result = await adapter.MoveMouseAsync(new ScreenPoint(0, 1080), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        interop.MovedTo.Should().Be(new ScreenPoint(0, 1080));
    }

    [Fact]
    public async Task ClickMouseAsyncEmitsMouseClicks()
    {
        var interop = new FakeWin32Interop();
        var adapter = new WindowsInputAdapter(interop);

        var result = await adapter.ClickMouseAsync(MouseButtonKind.Right, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        interop.ClickedButtons.Should().Equal(MouseButtonKind.Right);
    }

    [Fact]
    public async Task PressKeyAsyncHonorsKeyMetadata()
    {
        var interop = new FakeWin32Interop();
        var adapter = new WindowsInputAdapter(interop);
        var action = new KeyPressAction(0x87, 0x76, isExtendedKey: true, "F24");

        var result = await adapter.PressKeyAsync(action, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        interop.PressedKeys.Should().ContainSingle().Which.Should().Be(action);
    }

    [Fact]
    public async Task ZeroEventInjectionReturnsABlockedInputMessage()
    {
        var interop = new FakeWin32Interop
        {
            MoveResult = InputAdapterResult.Failure(
                InputFailureKind.BlockedByWindows,
                "Failed to move the mouse. Windows blocked the injected input."),
        };
        var adapter = new WindowsInputAdapter(interop);

        var result = await adapter.MoveMouseAsync(new ScreenPoint(10, 10), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("blocked");
        result.FailureKind.Should().Be(InputFailureKind.BlockedByWindows);
    }

    private sealed class FakeWin32Interop : IWin32Interop
    {
        public CursorLocationResult CursorResult { get; init; } = CursorLocationResult.Success(default);

        public InputAdapterResult MoveResult { get; init; } = InputAdapterResult.Success();

        public InputAdapterResult ClickResult { get; init; } = InputAdapterResult.Success();

        public InputAdapterResult KeyResult { get; init; } = InputAdapterResult.Success();

        public ScreenPoint CursorPosition { get; set; }

        public ScreenPoint? MovedTo { get; private set; }

        public List<MouseButtonKind> ClickedButtons { get; } = [];

        public List<KeyPressAction> PressedKeys { get; } = [];

        public CursorLocationResult GetCursorPosition()
            => CursorResult.Succeeded
                ? CursorLocationResult.Success(CursorPosition)
                : CursorResult;

        public InputAdapterResult SendMouseMove(ScreenPoint position)
        {
            MovedTo = position;
            return MoveResult;
        }

        public InputAdapterResult SendMouseClick(MouseButtonKind button)
        {
            ClickedButtons.Add(button);
            return ClickResult;
        }

        public InputAdapterResult SendKeyPress(KeyPressAction action)
        {
            PressedKeys.Add(action);
            return KeyResult;
        }

        public bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey)
            => true;

        public bool UnregisterHotKey(nint windowHandle, int id)
            => true;
    }
}
