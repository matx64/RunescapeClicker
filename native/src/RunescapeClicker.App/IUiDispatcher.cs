namespace RunescapeClicker.App;

public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}
