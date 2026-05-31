namespace Peerfluence.Core;

public interface IFeatureViewModel
{
    string Title { get; }

    string IconKind { get; }

    int Order { get; }
}
