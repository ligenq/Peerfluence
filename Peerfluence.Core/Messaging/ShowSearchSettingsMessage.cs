namespace Peerfluence.Core.Messaging;

/// <summary>
/// Take the user to where search is set up.
///
/// <para>
/// Sent by the Find torrents screen, which is where people discover they need an indexer and where
/// they find out theirs is not answering. Telling someone their problem is in the settings and
/// leaving them to find the settings is half an answer.
/// </para>
/// </summary>
public sealed class ShowSearchSettingsMessage;
