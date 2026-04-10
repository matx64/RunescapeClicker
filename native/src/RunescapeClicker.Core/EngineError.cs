namespace RunescapeClicker.Core;

public sealed record EngineError(
    EngineErrorCode Code,
    string Message,
    int? ActionIndex = null,
    InputFailureKind FailureKind = InputFailureKind.None);

public enum EngineErrorCode
{
    InputAdapterUnavailable = 0,
    MouseMoveFailed = 1,
    MouseClickFailed = 2,
    KeyPressFailed = 3,
}
