namespace Peerfluence.Core.Services;

public interface IWindowsAssociationService
{
    bool IsSupported { get; }

    bool IsTorrentFileAssociated { get; }

    bool IsMagnetLinkAssociated { get; }

    void ApplyAssociations(bool associateTorrentFiles, bool associateMagnetLinks);
}
