#nullable enable

namespace VividSoul.Runtime.Platform
{
    public interface IFileDialogService
    {
        string? OpenModelFile(string initialDirectory = "");

        string? OpenAnimationFile(string initialDirectory = "");

        string? OpenAnimationFolder(string initialDirectory = "");

        string? OpenBehaviorManifestFile(string initialDirectory = "");
    }
}
