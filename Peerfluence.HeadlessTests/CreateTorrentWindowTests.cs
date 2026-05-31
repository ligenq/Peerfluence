using Avalonia.Controls;
using Avalonia.LogicalTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

public class CreateTorrentWindowTests
{
    [AvaloniaFact]
    public void View_CanBeCreated()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public void PieceSizes_Has7Options()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(7, vm.PieceSizes.Count);
    }

    [AvaloniaFact]
    public void ErrorMessage_HiddenByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.ErrorMessage);
    }

    [AvaloniaFact]
    public void ErrorMessage_HasError_WhenSet()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.ErrorMessage = "Something went wrong";
        Assert.True(vm.HasError);
    }

    [AvaloniaFact]
    public void IsCreating_FalseByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.IsCreating);
    }

    [AvaloniaFact]
    public void CreateCommand_CannotExecute_WhenSourceEmpty()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(string.Empty, vm.SourcePath);
        Assert.False(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CreateCommand_CanExecute_WhenSourceSet()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.SourcePath = @"C:\some\path";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CreateCommand_CannotExecute_WhenCreating()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        vm.SourcePath = @"C:\some\path";
        vm.IsCreating = true;
        Assert.False(vm.CreateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void DefaultPieceSizeIndex_Is2()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.Equal(2, vm.SelectedPieceSizeIndex);
    }

    [AvaloniaFact]
    public void IsPrivate_FalseByDefault()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        Assert.False(vm.IsPrivate);
    }

    [AvaloniaFact]
    public void SourcePath_TextBox_IsReadOnly()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };

        // Apply template to instantiate controls
        view.ApplyTemplate();

        var textBoxes = view.GetLogicalDescendants().OfType<TextBox>().Where(tb => tb.IsReadOnly).ToList();
        Assert.NotEmpty(textBoxes);
    }

    [AvaloniaFact]
    public void MultilineTextBoxes_Exist()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        view.ApplyTemplate();

        var multiline = view.GetLogicalDescendants().OfType<TextBox>().Where(tb => tb.AcceptsReturn).ToList();
        // Trackers and WebSeeds
        Assert.True(multiline.Count >= 2, $"Expected at least 2 multiline TextBoxes, found {multiline.Count}");
    }

    [AvaloniaFact]
    public void CreateButton_TracksCommandCanExecute()
    {
        var vm = TestHelpers.CreateCreateTorrentViewModel();
        var view = new CreateTorrentWindow { DataContext = vm };
        view.ApplyTemplate();

        var createButton = view.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => ReferenceEquals(button.Command, vm.CreateCommand));

        Assert.False(createButton.IsEffectivelyEnabled);

        vm.SourcePath = @"C:\source";
        Assert.True(createButton.IsEffectivelyEnabled);

        vm.IsCreating = true;
        Assert.False(createButton.IsEffectivelyEnabled);
    }
}
