namespace Fm4StaticRecomp.Launcher.Tests;

public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
