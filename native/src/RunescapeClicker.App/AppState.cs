namespace RunescapeClicker.App;

public enum AppState
{
    Idle = 0,
    CapturingCoordinate = 1,
    EditingAction = 2,
    ReadyToRun = 3,
    Running = 4,
    Stopping = 5,
    Faulted = 6,
}
