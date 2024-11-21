
namespace Rynco.Rikki.Util;

/// <summary>
/// A simple class to defer an action until the end of a scope.
/// </summary>
/// <param name="action"></param>
public readonly struct AsyncDefer(Func<ValueTask> action) : IAsyncDisposable
{
    private readonly Func<ValueTask> action = action;

    public ValueTask DisposeAsync()
    {
        return this.action();
    }

    public static AsyncDefer Do(Func<ValueTask> action)
    {
        return new AsyncDefer(action);
    }
}


/// <summary>
/// A simple class to defer an action until the end of a scope.
/// </summary>
/// <param name="action"></param>
public readonly struct Defer(Action action) : IDisposable
{
    private readonly Action action = action;

    public void Dispose()
    {
        this.action();
    }

    public static Defer Do(Action action)
    {
        return new Defer(action);
    }
}
