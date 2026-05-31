using System;
using System.Collections.Generic;

namespace Peerfluence.Services.Mcp;

public static class McpConstants
{
    public const string Name = "Peerfluence";
    public const string Version = "1.0.0";

    // Tool Names
    public const string ToolAddTorrent = "add_torrent";
    public const string ToolAddTorrentDescription = "Adds a new torrent from a magnet link, file path, or base64 .torrent data.";
    public const string ToolManageTorrent = "manage_torrent";
    public const string ToolManageTorrentDescription = "Pause, resume, or remove a torrent by its info hash.";
    public const string ToolTakeScreenshot = "take_screenshot";
    public const string ToolTakeScreenshotDescription = "Captures a screenshot of the current application window.";
    public const string ToolShutdownApplication = "shutdown_application";
    public const string ToolShutdownApplicationDescription = "Gracefully shuts down the Peerfluence application.";
    public const string ToolUpdateSettings = "update_settings";
    public const string ToolUpdateSettingsDescription = "Updates application settings using a JSON object containing the changed properties.";
    public const string ToolInvokeUiAction = "invoke_ui_action";
    public const string ToolInvokeUiActionDescription = "Invokes a specific UI action like 'pause_all' or 'resume_all'.";
    public const string ToolGetTorrentDiagnostics = "get_torrent_diagnostics";
    public const string ToolGetTorrentDiagnosticsDescription = "Gets detailed diagnostic information for a specific torrent (trackers, peers, missing pieces).";
    public const string ToolSetFilePriority = "set_file_priority";
    public const string ToolSetFilePriorityDescription = "Sets the download priority for a specific file within a torrent.";
    public const string ToolGetUiTestState = "ui_agent_get_state";
    public const string ToolGetUiTestStateDescription = "Gets structured UI-agent test state, including window status, selection, and visible torrents.";
    public const string ToolLoadTorrentFile = "ui_agent_load_torrent_file";
    public const string ToolLoadTorrentFileDescription = "Loads a .torrent file through the running application for UI-agent tests.";
    public const string ToolResumeTorrent = "ui_agent_resume_torrent";
    public const string ToolResumeTorrentDescription = "Starts or resumes a torrent identified by info hash, exact name, or unique partial name.";
    public const string ToolStopTorrent = "ui_agent_stop_torrent";
    public const string ToolStopTorrentDescription = "Stops a torrent identified by info hash, exact name, or unique partial name.";
    public const string ToolSelectTorrent = "ui_agent_select_torrent";
    public const string ToolSelectTorrentDescription = "Selects a torrent in the running application by info hash, exact name, or unique partial name.";
    public const string ToolWaitForTorrent = "ui_agent_wait_for_torrent";
    public const string ToolWaitForTorrentDescription = "Waits until a torrent reaches a requested state and/or minimum progress percentage.";
    public const string ToolAssertTorrent = "ui_agent_assert_torrent";
    public const string ToolAssertTorrentDescription = "Asserts a torrent's current state and progress bounds.";
    public const string ToolCleanup = "ui_agent_cleanup";
    public const string ToolCleanupDescription = "Cleans up UI-agent test state by removing torrents and/or clearing the selected torrent.";
    public const string ToolGetTimeline = "ui_agent_get_timeline";
    public const string ToolGetTimelineDescription = "Gets the UI-agent action/assertion timeline for failure debugging.";
    public const string ToolClearTimeline = "ui_agent_clear_timeline";
    public const string ToolClearTimelineDescription = "Clears the UI-agent action/assertion timeline.";

    // Resource Names
    public const string ResourceLogsLatest = "logs://latest";
    public const string ResourceLogsLatestDescription = "The latest application log entries.";
    public const string ResourceEngineStats = "engine://stats";
    public const string ResourceEngineStatsDescription = "Real-time aggregate BitTorrent engine statistics.";
    public const string ResourceActiveTorrents = "engine://torrents/active";
    public const string ResourceActiveTorrentsDescription = "A list of currently active torrents and their status.";
    public const string ResourceRecentAlerts = "engine://alerts/recent";
    public const string ResourceRecentAlertsDescription = "The most recent BitTorrent engine alerts.";
    public const string ResourceTorrentFiles = "torrent://{infoHash}/files";
    public const string ResourceTorrentFilesDescription = "A list of files and their download progress for a specific torrent.";
    public const string ResourceTorrentPeers = "torrent://{infoHash}/peers";
    public const string ResourceTorrentPeersDescription = "A list of connected peers and their status for a specific torrent.";

    // Prompt Names
    public const string PromptPerformanceAudit = "performance_audit";
    public const string PromptPerformanceAuditDescription = "Analyzes engine statistics and peer connections to suggest optimizations.";
    public const string PromptCrashInvestigator = "crash_investigator";
    public const string PromptCrashInvestigatorDescription = "Examines recent logs and alerts to identify the cause of application issues.";
    public const string PromptUiTestCaseRunner = "ui_test_case_runner";
    public const string PromptUiTestCaseRunnerDescription = "Guides an AI agent through executing natural-language UI test cases against the running application.";

    // Response Types for NativeAOT JSON Source Generation

    public record EngineStatsResponse(
        long TotalDownloaded,
        long TotalUploaded,
        int TotalPeers,
        int ActiveTorrents,
        int DownloadSpeed,
        int UploadSpeed,
        int TorrentCount
    );

    public record TorrentListResponse(
        List<TorrentSummary> Torrents
    );

    public record TorrentSummary(
        string Name,
        string Hash,
        string State,
        float Progress,
        long DownloadSpeed,
        long UploadSpeed,
        int Peers,
        int Seeds,
        long Size
    );

    public record UiAgentStateResponse(
        bool WindowAvailable,
        string? SelectedTorrentHash,
        string? SelectedTorrentName,
        List<TorrentSummary> Torrents
    );

    public record UiAgentWaitResult(
        bool Matched,
        string Message,
        TorrentSummary? Torrent
    );

    public record UiAgentAssertionResult(
        bool Passed,
        string Message,
        TorrentSummary? Torrent
    );

    public record UiAgentCleanupResult(
        int RemovedTorrents,
        bool SelectionCleared
    );

    public record UiAgentTimelineEvent(
        DateTimeOffset Timestamp,
        string EventType,
        string Message
    );

    public record AlertSummary(
        DateTimeOffset Timestamp,
        string AlertType,
        string TorrentHash,
        string TorrentName,
        string Message
    );

    public record TorrentFileSummary(
        int Index,
        string Path,
        long Size,
        string Priority,
        float Progress,
        bool IsFinished
    );

    public record TorrentPeerSummary(
        string EndPoint,
        string ClientName,
        string? Country,
        long DownloadSpeed,
        long UploadSpeed,
        float Progress,
        bool IsUtp,
        bool IsEncrypted,
        PeerFlags Flags
    );

    public record PeerFlags(
        bool AmChoking,
        bool AmInterested,
        bool PeerChoking,
        bool PeerInterested
    );

    public record TorrentDiagnosticsResponse(
        string Name,
        string Hash,
        string State,
        string? Exception,
        int PieceCount,
        int MissingPieces,
        List<TrackerDiagnostic> Trackers,
        List<PeerDiagnostic> Peers
    );

    public record TrackerDiagnostic(
        string Url,
        string Status,
        int ConsecutiveFailures,
        string? LastError,
        DateTimeOffset NextAnnounce
    );

    public record PeerDiagnostic(
        string EndPoint,
        string ClientName,
        bool IsUtp,
        bool IsEncrypted,
        long DownloadSpeed,
        long UploadSpeed,
        PeerFlags Flags,
        float Progress
    );

    public record McpOperationResult(
        bool Success,
        string Message,
        string? Code = null
    );
}
