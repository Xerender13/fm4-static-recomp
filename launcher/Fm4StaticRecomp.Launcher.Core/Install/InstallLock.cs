namespace Fm4StaticRecomp.Launcher.Core.Install;

// One launcher per install root: two would build into the same source folder, extract over
// each other's game.partial and start the game twice on one profile. The OS drops the lock
// when the process ends, however it ends.
public sealed class InstallLock : IDisposable
{
    private const string FileName = "launcher.lock";
    private readonly FileStream _file;

    private InstallLock(FileStream file) => _file = file;

    public static InstallLock? TryAcquire(string root, TimeSpan wait)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, FileName);
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                return new InstallLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public void Dispose() => _file.Dispose();
}
