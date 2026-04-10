using FluentAssertions;
using RunescapeClicker.Core;
namespace RunescapeClicker.Automation.Windows.Tests;

public sealed class GlobalHotkeyServiceTests
{
    private const int WindowMessageHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF1 = 0x70;
    private const uint VirtualKeyF2 = 0x71;

    [Fact]
    public async Task EnsureRegisteredAsyncRegistersF1AndF2WithNoRepeat()
    {
        var host = new FakeAutomationWindowHost();
        var interop = new FakeWin32Interop();
        using var service = new GlobalHotkeyService(host, interop);

        var result = await service.EnsureRegisteredAsync(CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        interop.RegisteredHotkeys.Should().HaveCount(2);
        interop.RegisteredHotkeys.Should().Contain((0x1001, ModNoRepeat, VirtualKeyF1));
        interop.RegisteredHotkeys.Should().Contain((0x1002, ModNoRepeat, VirtualKeyF2));
    }

    [Fact]
    public async Task EnsureRegisteredAsyncReturnsFriendlyMessageWhenRegistrationFails()
    {
        var host = new FakeAutomationWindowHost();
        var interop = new FakeWin32Interop
        {
            FailF2Registration = true,
        };
        using var service = new GlobalHotkeyService(host, interop);

        var result = await service.EnsureRegisteredAsync(CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("F2");
        interop.UnregisteredHotkeys.Should().Contain(0x1001);
    }

    [Fact]
    public async Task WMHotkeyRaisesTypedEventsAndIgnoresUnknownIds()
    {
        var host = new FakeAutomationWindowHost();
        var interop = new FakeWin32Interop();
        using var service = new GlobalHotkeyService(host, interop);
        await service.EnsureRegisteredAsync(CancellationToken.None);

        var received = new List<AutomationHotkey>();
        service.HotkeyPressed += (_, args) => received.Add(args.Hotkey);

        host.RaiseMessage(new WindowMessage(WindowMessageHotkey, 0x1001, 0));
        host.RaiseMessage(new WindowMessage(WindowMessageHotkey, 0x1002, 0));
        host.RaiseMessage(new WindowMessage(WindowMessageHotkey, 0x9999, 0));

        received.Should().Equal(AutomationHotkey.CaptureCursor, AutomationHotkey.StopRun);
    }

    [Fact]
    public async Task DisposeUnregistersBothHotkeys()
    {
        var host = new FakeAutomationWindowHost();
        var interop = new FakeWin32Interop();
        var service = new GlobalHotkeyService(host, interop);
        await service.EnsureRegisteredAsync(CancellationToken.None);

        service.Dispose();

        interop.UnregisteredHotkeys.Should().Contain([0x1001, 0x1002]);
    }

    private sealed class FakeAutomationWindowHost : IAutomationWindowHost
    {
        public event EventHandler<WindowMessage>? WindowMessageReceived;

        public Task<nint> EnsureWindowHandleAsync(CancellationToken cancellationToken)
            => Task.FromResult<nint>(4242);

        public Task<CoordinatePickerResult> ShowCoordinatePickerAsync(CancellationToken cancellationToken)
            => Task.FromResult(CoordinatePickerResult.Busy());

        public void RaiseMessage(WindowMessage message)
            => WindowMessageReceived?.Invoke(this, message);

        public void Dispose()
        {
        }
    }

    private sealed class FakeWin32Interop : IWin32Interop
    {
        public bool FailF2Registration { get; init; }

        public List<(int Id, uint Modifiers, uint VirtualKey)> RegisteredHotkeys { get; } = [];

        public List<int> UnregisteredHotkeys { get; } = [];

        public CursorLocationResult GetCursorPosition() => CursorLocationResult.Success(default);

        public InputAdapterResult SendMouseMove(ScreenPoint position) => InputAdapterResult.Success();

        public InputAdapterResult SendMouseClick(MouseButtonKind button) => InputAdapterResult.Success();

        public InputAdapterResult SendKeyPress(KeyPressAction action) => InputAdapterResult.Success();

        public bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey)
        {
            RegisteredHotkeys.Add((id, modifiers, virtualKey));
            return !FailF2Registration || id != 0x1002;
        }

        public bool UnregisterHotKey(nint windowHandle, int id)
        {
            UnregisteredHotkeys.Add(id);
            return true;
        }
    }
}
