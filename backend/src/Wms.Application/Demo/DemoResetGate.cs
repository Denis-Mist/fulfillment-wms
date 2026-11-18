namespace Wms.Application.Demo;

public sealed class DemoResetGate : IDemoResetGate
{
    private int _state;
    public bool IsResetting => Volatile.Read(ref _state) != 0;
    public bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    public void End() => Volatile.Write(ref _state, 0);
}
