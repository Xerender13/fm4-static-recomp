using System.Text;

namespace Fm4StaticRecomp.Launcher.Core.Platform;

public sealed class LinuxPlatform : IPlatform
{
    public bool SupportsDxgiPresentation => false;

    public string ExecutableName(string baseName) => baseName;

    public Encoding ConsoleEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public void CreateDirectoryLink(string link, string target) => Directory.CreateSymbolicLink(link, target);

    public bool IsDirectoryLink(string path) => new DirectoryInfo(path).LinkTarget is not null;
}
