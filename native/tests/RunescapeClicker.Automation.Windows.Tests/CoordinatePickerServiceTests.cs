using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows.Tests;

public sealed class CoordinatePickerServiceTests
{
    [Fact]
    public void SessionStartsWaitingForReleaseAndArmsAfterRelease()
    {
        var session = new CoordinatePickerSession();

        session.WaitingForRelease.Should().BeTrue();
        session.TryArm(primaryButtonDown: true).Should().BeFalse();
        session.TryArm(primaryButtonDown: false).Should().BeTrue();
        session.WaitingForRelease.Should().BeFalse();
    }

    [Fact]
    public void SessionCapturesOnlyAfterTheInitialRelease()
    {
        var session = new CoordinatePickerSession();

        session.TryCapture(new ScreenPoint(10, 20)).Should().BeNull();
        session.TryArm(primaryButtonDown: false).Should().BeTrue();
        session.TryCapture(new ScreenPoint(10, 20)).Should().Be(CoordinatePickerResult.Captured(new ScreenPoint(10, 20)));
        session.TryCapture(new ScreenPoint(30, 40)).Should().BeNull();
    }

    [Fact]
    public void SessionCancelReturnsCancelledResult()
    {
        var session = new CoordinatePickerSession();

        session.Cancel().Outcome.Should().Be(CoordinatePickerOutcome.Cancelled);
    }

    [Fact]
    public async Task ServiceRejectsConcurrentPickerRequests()
    {
        var host = new BlockingAutomationWindowHost();
        using var service = new CoordinatePickerService(host);

        var firstRequest = service.PickCoordinateAsync(CancellationToken.None);
        var secondRequest = await service.PickCoordinateAsync(CancellationToken.None);

        secondRequest.Outcome.Should().Be(CoordinatePickerOutcome.Busy);
        host.Complete(CoordinatePickerResult.Captured(new ScreenPoint(1, 2)));
        (await firstRequest).Outcome.Should().Be(CoordinatePickerOutcome.Captured);
    }

    [Fact]
    public async Task ServiceReturnsTheHostResult()
    {
        var host = new BlockingAutomationWindowHost();
        using var service = new CoordinatePickerService(host);

        var request = service.PickCoordinateAsync(CancellationToken.None);
        host.Complete(CoordinatePickerResult.Cancelled());

        (await request).Outcome.Should().Be(CoordinatePickerOutcome.Cancelled);
    }

    private sealed class BlockingAutomationWindowHost : IAutomationWindowHost
    {
        private readonly TaskCompletionSource<CoordinatePickerResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<WindowMessage>? WindowMessageReceived
        {
            add { }
            remove { }
        }

        public Task<nint> EnsureWindowHandleAsync(CancellationToken cancellationToken)
            => Task.FromResult<nint>(99);

        public Task<CoordinatePickerResult> ShowCoordinatePickerAsync(CancellationToken cancellationToken)
            => _completion.Task;

        public void Complete(CoordinatePickerResult result) => _completion.TrySetResult(result);

        public void Dispose()
        {
        }
    }
}
