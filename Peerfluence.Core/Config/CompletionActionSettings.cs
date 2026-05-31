namespace Peerfluence.Core.Config;

public sealed class CompletionActionSettings
{
    public bool Enabled { get; set; }

    public string ProgramPath { get; set; } = string.Empty;

    public string ArgumentsTemplate { get; set; } = string.Empty;

    public string WorkingDirectoryTemplate { get; set; } = "{downloadPath}";

    public int TimeoutSeconds { get; set; } = 300;

    public bool RunHidden { get; set; } = true;
}
