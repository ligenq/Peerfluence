using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// A torrent's name is whatever the person who made it wrote, and it ends up as a folder name.
/// </summary>
public sealed class TorrentFolderTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "peerfluence-root");

    [Theory]
    [InlineData("Ubuntu 26.04 ISO", "Ubuntu 26.04 ISO")]
    [InlineData("album", "album")]
    public void AnOrdinaryName_IsTheFolder(string name, string folder)
    {
        Assert.Equal(Path.Combine(Root, folder), TorrentFolder.Within(Root, name));
    }

    /// <summary>
    /// The engine checks a torrent's files against the folder it is given, so a name that moves the
    /// folder takes those checks along with it, and the files can go anywhere.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../..")]
    [InlineData("..\\..\\Startup")]
    [InlineData("../../.config/autostart")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("/etc/cron.d")]
    [InlineData("\\\\server\\share")]
    [InlineData("a/../../b")]
    public void ANameThatTriesToLeave_StaysDirectlyInsideTheRoot(string name)
    {
        string folder = TorrentFolder.Within(Root, name);

        Assert.Equal(Path.GetFullPath(Root), Path.GetDirectoryName(Path.GetFullPath(folder)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("name:with?bad*chars")]
    [InlineData("trailing dot.")]
    [InlineData("trailing space ")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1")]
    public void ANameNoFolderCouldHave_BecomesOneThatCanExist(string name)
    {
        string folder = TorrentFolder.Within(Root, name);
        string last = Path.GetFileName(folder);

        Directory.CreateDirectory(Root);
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(last));
            Assert.DoesNotContain(last, c => Path.GetInvalidFileNameChars().Contains(c));
            Assert.False(last.EndsWith('.') || last.EndsWith(' '));
            Directory.CreateDirectory(folder);
            Assert.True(Directory.Exists(folder));
            Assert.Equal(last, new DirectoryInfo(folder).Name);
        }
        finally
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
