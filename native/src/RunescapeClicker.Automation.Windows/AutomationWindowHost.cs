using System.Drawing;
using System.Windows.Forms;
using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows;

internal readonly record struct WindowMessage(int MessageId, nint WParam, nint LParam);

internal interface IAutomationWindowHost : IDisposable
{
    event EventHandler<WindowMessage>? WindowMessageReceived;

    Task<nint> EnsureWindowHandleAsync(CancellationToken cancellationToken);

    Task<CoordinatePickerResult> ShowCoordinatePickerAsync(CancellationToken cancellationToken);
}

internal sealed class AutomationWindowHost : IAutomationWindowHost
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<nint> _handleReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _threadExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private HostApplicationContext? _context;
    private bool _disposed;
    private bool _shutdownRequested;

    public event EventHandler<WindowMessage>? WindowMessageReceived;

    public Task<nint> EnsureWindowHandleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureThreadStarted();
        return _handleReady.Task.WaitAsync(cancellationToken);
    }

    public async Task<CoordinatePickerResult> ShowCoordinatePickerAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureWindowHandleAsync(cancellationToken);
        var context = _context ?? throw new InvalidOperationException("The automation window host did not finish initializing.");
        return await context.ShowCoordinatePickerAsync(cancellationToken);
    }

    public void Dispose()
    {
        HostApplicationContext? context;
        bool shouldWaitForThread;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownRequested = true;
            context = _context;
            shouldWaitForThread = _thread is not null;
        }

        if (context is not null)
        {
            context.RequestShutdown();
        }

        if (shouldWaitForThread)
        {
            _threadExited.Task.Wait(TimeSpan.FromSeconds(5));
            (_context ?? context)?.Dispose();
        }
    }

    private void EnsureThreadStarted()
    {
        lock (_gate)
        {
            if (_thread is not null)
            {
                return;
            }

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "RunescapeClicker.Automation.WindowsHost",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    private void ThreadMain()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var context = new HostApplicationContext(OnWindowMessageReceived, _handleReady);
            lock (_gate)
            {
                _context = context;
                if (_shutdownRequested)
                {
                    context.RequestShutdown();
                }
            }

            Application.Run(context);
        }
        finally
        {
            _threadExited.TrySetResult();
        }
    }

    private void OnWindowMessageReceived(object? sender, WindowMessage message)
        => WindowMessageReceived?.Invoke(this, message);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class HostApplicationContext : ApplicationContext
    {
        private readonly HostMessageWindow _messageWindow;
        private readonly SynchronizationContext _synchronizationContext;
        private CoordinatePickerForm? _activePicker;

        public HostApplicationContext(
            EventHandler<WindowMessage> messageHandler,
            TaskCompletionSource<nint> handleReady)
        {
            _messageWindow = new HostMessageWindow();
            _messageWindow.WindowMessageReceived += messageHandler;
            _messageWindow.Handle.ToInt64();
            _synchronizationContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("A Windows Forms synchronization context is required.");
            handleReady.TrySetResult(_messageWindow.Handle);
        }

        public Task<CoordinatePickerResult> ShowCoordinatePickerAsync(CancellationToken cancellationToken)
        {
            if (_activePicker is not null)
            {
                return Task.FromResult(CoordinatePickerResult.Busy());
            }

            var completion = new TaskCompletionSource<CoordinatePickerResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            PostToUiThread(() =>
            {
                if (_activePicker is not null)
                {
                    completion.TrySetResult(CoordinatePickerResult.Busy());
                    return;
                }

                var picker = new CoordinatePickerForm();
                _activePicker = picker;

                picker.Completed += (_, result) =>
                {
                    _activePicker = null;
                    completion.TrySetResult(result);
                    picker.Dispose();
                };

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => PostToUiThread(() => picker.CancelPicker()));
                }

                picker.Show();
                picker.Activate();
            });

            return completion.Task;
        }

        public void RequestShutdown() => PostToUiThread(ExitThread);

        protected override void ExitThreadCore()
        {
            _activePicker?.CancelPicker();
            _activePicker?.Dispose();
            _messageWindow.Dispose();
            base.ExitThreadCore();
        }

        private void PostToUiThread(Action action)
            => _synchronizationContext.Post(_ => action(), null);
    }

    private sealed class HostMessageWindow : Form
    {
        public event EventHandler<WindowMessage>? WindowMessageReceived;

        public HostMessageWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(-32000, -32000, 1, 1);
            Opacity = 0;
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

        protected override void WndProc(ref Message m)
        {
            WindowMessageReceived?.Invoke(this, new WindowMessage(m.Msg, m.WParam, m.LParam));
            base.WndProc(ref m);
        }
    }

    private sealed class CoordinatePickerForm : Form
    {
        private readonly CoordinatePickerSession _session = new();
        private readonly System.Windows.Forms.Timer _timer;
        private ScreenPoint? _preview;
        private bool _completed;

        public CoordinatePickerForm()
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            Bounds = virtualScreen;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Opacity = 0.45;
            Cursor = Cursors.Cross;

            _timer = new System.Windows.Forms.Timer
            {
                Interval = 16,
            };
            _timer.Tick += (_, _) => TickPreview();
            _timer.Start();
        }

        public event EventHandler<CoordinatePickerResult>? Completed;

        public void CancelPicker()
        {
            if (_completed)
            {
                return;
            }

            Complete(_session.Cancel());
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TickPreview();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                CancelPicker();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && !_session.WaitingForRelease)
            {
                var point = PointToScreen(e.Location);
                var result = _session.TryCapture(new ScreenPoint(point.X, point.Y));
                if (result is not null)
                {
                    Complete(result);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using var crosshairPen = new Pen(Color.LimeGreen, 1);
            using var hudBrush = new SolidBrush(Color.White);
            using var hudBackground = new SolidBrush(Color.FromArgb(210, 16, 16, 16));
            using var instructionFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            using var detailFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);

            var instructions = _session.WaitingForRelease
                ? "Release the mouse button to arm capture."
                : "Click anywhere to capture coordinates. Press Esc to cancel.";
            var coordinateText = _preview is null
                ? "Move the cursor to preview coordinates."
                : $"Preview: ({_preview.Value.X}, {_preview.Value.Y})";

            var hudRectangle = new Rectangle(24, 24, 440, 84);
            e.Graphics.FillRectangle(hudBackground, hudRectangle);
            e.Graphics.DrawString(instructions, instructionFont, hudBrush, hudRectangle.Left + 12, hudRectangle.Top + 10);
            e.Graphics.DrawString(coordinateText, detailFont, hudBrush, hudRectangle.Left + 12, hudRectangle.Top + 44);

            if (_preview is null)
            {
                return;
            }

            var clientPoint = PointToClient(new Point(_preview.Value.X, _preview.Value.Y));
            e.Graphics.DrawLine(crosshairPen, 0, clientPoint.Y, Width, clientPoint.Y);
            e.Graphics.DrawLine(crosshairPen, clientPoint.X, 0, clientPoint.X, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void TickPreview()
        {
            _preview = new ScreenPoint(Cursor.Position.X, Cursor.Position.Y);
            _session.TryArm((Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left);
            Invalidate();
        }

        private void Complete(CoordinatePickerResult result)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _timer.Stop();
            Completed?.Invoke(this, result);
            Close();
        }
    }
}
