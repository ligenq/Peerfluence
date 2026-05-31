using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

public interface IThemeService
{
    void Apply(ThemeSettings settings);
}
