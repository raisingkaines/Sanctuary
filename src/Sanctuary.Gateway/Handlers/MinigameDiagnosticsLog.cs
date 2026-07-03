using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sanctuary.Gateway.Handlers;

internal static class MinigameDiagnosticsLog
{
	private const int MaxHexChars = 2048;

	private const int MaxTextChars = 1024;

	private static ILogger _logger;

	private static bool _announced;

	private static readonly object LaunchDebugLock = new object();

	private static bool VerboseDebug
	{
		get
		{
			string environmentVariable = Environment.GetEnvironmentVariable("TCG_VERBOSE_DEBUG");
			if (!string.Equals(environmentVariable, "1", StringComparison.OrdinalIgnoreCase) && !string.Equals(environmentVariable, "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(environmentVariable, "yes", StringComparison.OrdinalIgnoreCase))
			{
				return string.Equals(environmentVariable, "on", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		if (_logger == null)
		{
			_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MinigameDiagnostics");
		}
		if (!_announced)
		{
			_announced = true;
			Info("diagnostics-online", "MinigameDiagnosticsLog", 0uL, Field("format", "pipe-delimited"), Field("prefix", "MINIGAME_DIAG"), Field("log", "Logs/MinigameDiagnostics-${shortdate}.log"), Field("tcgLaunchDebugLog", "Logs/minigame_tcg_launch_debug.log"));
		}
	}

	public static void Info(string eventName, string handler, ulong playerGuid, bool worldTunnel, params string[] fields)
	{
		Write(LogLevel.Information, eventName, handler, playerGuid, worldTunnel ? "true" : "false", fields);
	}

	public static void Warn(string eventName, string handler, ulong playerGuid, bool worldTunnel, params string[] fields)
	{
		Write(LogLevel.Warning, eventName, handler, playerGuid, worldTunnel ? "true" : "false", fields);
	}

	public static void Info(string eventName, string handler, ulong playerGuid, params string[] fields)
	{
		Write(LogLevel.Information, eventName, handler, playerGuid, "n/a", fields);
	}

	public static void Warn(string eventName, string handler, ulong playerGuid, params string[] fields)
	{
		Write(LogLevel.Warning, eventName, handler, playerGuid, "n/a", fields);
	}

	public static string Field(string name, object value)
	{
		return Clean(name) + "=" + Clean(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
	}

	public static string HexField(string name, ReadOnlySpan<byte> bytes)
	{
		string text = Convert.ToHexString(bytes);
		bool flag = text.Length > 2048;
		if (flag)
		{
			text = text.Substring(0, 2048);
		}
		return $"{Clean(name)}={text};{Clean(name)}Truncated={flag.ToString().ToLowerInvariant()}";
	}

	public static string Fields(params string[] fields)
	{
		return string.Join("|", fields);
	}

	public static void ClientLog(ulong playerGuid, string text)
	{
		if (LooksMinigameRelated(text))
		{
			Write(LooksLikeError(text) ? LogLevel.Warning : LogLevel.Information, "client-log", "PacketClientLogHandler", playerGuid, "n/a", Field("clientLog", Limit(text, 1024)));
		}
	}

	public static bool LooksMinigameRelated(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (!text.Contains("minigame", StringComparison.OrdinalIgnoreCase) && !text.Contains("mini game", StringComparison.OrdinalIgnoreCase) && !text.Contains("tcg", StringComparison.OrdinalIgnoreCase) && !text.Contains("trading card", StringComparison.OrdinalIgnoreCase) && !text.Contains("cardgame", StringComparison.OrdinalIgnoreCase) && !text.Contains("FreeRealmsTCG", StringComparison.OrdinalIgnoreCase) && !text.Contains("lobbygame", StringComparison.OrdinalIgnoreCase) && !text.Contains("matchmaking", StringComparison.OrdinalIgnoreCase) && !text.Contains("activitylaunch", StringComparison.OrdinalIgnoreCase) && !text.Contains("one time session", StringComparison.OrdinalIgnoreCase) && !text.Contains("ActivityPortal", StringComparison.OrdinalIgnoreCase) && !text.Contains("MinigameDetail", StringComparison.OrdinalIgnoreCase) && !text.Contains("MiniGameStateManager", StringComparison.OrdinalIgnoreCase) && !text.Contains("HorzItem", StringComparison.OrdinalIgnoreCase) && !text.Contains("addHorzItem", StringComparison.OrdinalIgnoreCase) && !text.Contains("PlayButtonClicked", StringComparison.OrdinalIgnoreCase) && !text.Contains("RequestActivity", StringComparison.OrdinalIgnoreCase) && !text.Contains("No Game Selected", StringComparison.OrdinalIgnoreCase) && !text.Contains("Please Select a Game", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("onetime", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool LooksLikeError(string text)
	{
		if (!text.Contains("error", StringComparison.OrdinalIgnoreCase) && !text.Contains("fail", StringComparison.OrdinalIgnoreCase) && !text.Contains("exception", StringComparison.OrdinalIgnoreCase) && !text.Contains("timeout", StringComparison.OrdinalIgnoreCase) && !text.Contains("corrupt", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("mismatch", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static void Write(LogLevel level, string eventName, string handler, ulong playerGuid, string worldTunnel, params string[] fields)
	{
		if (_logger == null || (level < LogLevel.Warning && !VerboseDebug && !IsReleaseDiagnosticEvent(eventName, handler, fields)))
		{
			return;
		}
		string text = $"MINIGAME_DIAG|event={Clean(eventName)}|handler={Clean(handler)}|player={playerGuid}|worldTunnel={worldTunnel}";
		foreach (string text2 in fields)
		{
			if (!string.IsNullOrWhiteSpace(text2))
			{
				text = text + "|" + text2;
			}
		}
		if (level >= LogLevel.Warning)
		{
			_logger.LogWarning("{diagnostic}", text);
		}
		else
		{
			_logger.LogInformation("{diagnostic}", text);
		}
		WriteTcgLaunchDebug(level, eventName, handler, playerGuid, worldTunnel, fields);
	}

	private static void WriteTcgLaunchDebug(LogLevel level, string eventName, string handler, ulong playerGuid, string worldTunnel, string[] fields)
	{
		if ((level < LogLevel.Warning && !VerboseDebug) || !IsTcgLaunchDebugEvent(eventName, handler, fields))
		{
			return;
		}
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "Logs");
			Directory.CreateDirectory(text);
			string value = GetFieldValue(fields, "selectedGame") ?? "Trading Card Game";
			string value2 = GetFieldValue(fields, "selectedId") ?? Convert.ToString(387, CultureInfo.InvariantCulture);
			string value3 = GetFieldValue(fields, "activityId") ?? Convert.ToString(7, CultureInfo.InvariantCulture);
			string text2 = string.Format(CultureInfo.InvariantCulture, "timestamp={0:O}|selectedGame={1}|selectedId={2}|activityId={3}|routeOrHandler={4}|result={5}|level={6}|player={7}|worldTunnel={8}", DateTimeOffset.Now, Clean(value), Clean(value2), Clean(value3), Clean(handler), Clean(eventName), level, playerGuid, worldTunnel);
			if (level >= LogLevel.Warning)
			{
				text2 += "|error=true";
			}
			string[] array = fields ?? Array.Empty<string>();
			foreach (string text3 in array)
			{
				if (!string.IsNullOrWhiteSpace(text3))
				{
					text2 = text2 + "|" + text3;
				}
			}
			lock (LaunchDebugLock)
			{
				File.AppendAllText(Path.Combine(text, "minigame_tcg_launch_debug.log"), text2 + Environment.NewLine);
			}
		}
		catch
		{
		}
	}

	private static bool IsTcgLaunchDebugEvent(string eventName, string handler, string[] fields)
	{
		if (IsRequiredUiEvent(eventName))
		{
			return true;
		}
		if (ContainsTcgLaunchHint(eventName) || ContainsTcgLaunchHint(handler))
		{
			return true;
		}
		string[] array = fields ?? Array.Empty<string>();
		foreach (string text in array)
		{
			if (ContainsTcgLaunchHint(text) || text.Contains("activity=7", StringComparison.OrdinalIgnoreCase) || text.Contains("queue=41", StringComparison.OrdinalIgnoreCase) || text.Contains("miniGameId=387", StringComparison.OrdinalIgnoreCase) || text.Contains("requestedGameId=387", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsRequiredUiEvent(string eventName)
	{
		if (!string.Equals(eventName, "GamesMenuOpened", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "GameGridLoaded", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "ListQueuesRequest", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "TcgCategoryDataSent", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "TcgMinigameUnlockRowsSent", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "GameTileClicked", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "GameSelected", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "GameDetailPanelUpdated", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "NoGameSelectedShown", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "PlayClicked", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "ActivityInviteSent", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "ActivityInviteAccepted", StringComparison.OrdinalIgnoreCase) && !string.Equals(eventName, "TradingCardGameHandlerStarted", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(eventName, "TcgNativeLaunchStarted", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsReleaseDiagnosticEvent(string eventName, string handler, string[] fields)
	{
		if (string.IsNullOrWhiteSpace(eventName))
		{
			return false;
		}
		if (string.Equals(eventName, "diagnostics-online", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "PacketReadyActivityDefinitionsSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "PacketReadyStartupCategoryDetailUsed", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "OldTcgOverrideSuppressed", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "PacketReadyOpcode167AfterStartup", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "PacketReadyActivitiesCategory11MutationAfterStartup", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "GenericMinigameStartScreenPacketsSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "GenericMinigameStartScreenSkipped", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgActivityWireRow", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgMiniGameGroupElementWireRow", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgLobbyDefinitionWireRow", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "BrowserV2AddGameBoundaryRow", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "OuterGamesGridRowCandidate", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenMiniGameInfoSeedSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgMiniGameInfoUnknown13Sent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenVisualStatePacket", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenActivityLaunchPreludeSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenSanctuaryPreludeOnly", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenSetLoadingFalseCandidateScheduled", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenSetLoadingFalseCandidateSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenSetLoadingFalseCandidateSkipped", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "MinigameStartScreenStringCommandSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgAssetsPrepareStarted", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgWaitingForClientAssetsReady", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgAssetsReadyTimeoutFallbackScheduled", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgAssetsReadyTimeoutFallback", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgAssetsReadyTimeoutFallbackSkipped", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgClientAssetsReadyObserved", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgClientAssetsReadyResponseSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgPostGoClientAssetsReadyRequestObserved", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgPostGoClientAssetsReadyResponseSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenReadyTransitionScheduled", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenReadyNativeCreateGameResultSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenReadyMiniGameGameStartEchoSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenReadyNative387CandidateSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenReadyVisualFallbackSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgGoVisible", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "MiniGameCreateGameResultPacketSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenMetadataSelected", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "MiniGameJoinGamePacketSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgPlaySelectedDetailRow", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "GameTileClicked", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "GameSelected", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenOpened", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenGoClickedRequestObserved", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenGoStartAction", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenGoDuplicateSuppressed", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgPostGoLaunchIdentity", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgGoLaunchStarted", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgGoLaunchCompleted", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgGoLaunchCommandsSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgStartScreenGoLaunchCommandsSent", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TcgNativeLaunchStarted", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "TradingCardGameHandlerStarted", StringComparison.OrdinalIgnoreCase) || string.Equals(eventName, "tcg-accepted-launch-sent", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (LooksLikeError(eventName) || ContainsConnectionHint(eventName) || ContainsConnectionHint(handler))
		{
			return true;
		}
		string[] array = fields ?? Array.Empty<string>();
		foreach (string text in array)
		{
			if (LooksLikeError(text) || ContainsConnectionHint(text))
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsConnectionHint(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (!value.Contains("connect", StringComparison.OrdinalIgnoreCase) && !value.Contains("disconnect", StringComparison.OrdinalIgnoreCase) && !value.Contains("handshake", StringComparison.OrdinalIgnoreCase) && !value.Contains("ticket", StringComparison.OrdinalIgnoreCase) && !value.Contains("FreeRealmsTCG", StringComparison.OrdinalIgnoreCase) && !value.Contains("TcgTunnel", StringComparison.OrdinalIgnoreCase) && !value.Contains("TcgServerAddress", StringComparison.OrdinalIgnoreCase))
		{
			return value.Contains("TCG Start command line", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string GetFieldValue(string[] fields, string name)
	{
		string text = name + "=";
		string[] array = fields ?? Array.Empty<string>();
		foreach (string text2 in array)
		{
			if (text2 != null && text2.StartsWith(text, StringComparison.OrdinalIgnoreCase))
			{
				return text2.Substring(text.Length);
			}
		}
		return null;
	}

	private static bool ContainsTcgLaunchHint(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			if (!value.Contains("tcg", StringComparison.OrdinalIgnoreCase) && !value.Contains("trading card", StringComparison.OrdinalIgnoreCase) && !value.Contains("TradingCardGame", StringComparison.OrdinalIgnoreCase))
			{
				return value.Contains("complete-tcg-launch", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	private static string Clean(string value)
	{
		return Limit(value, 1024).Replace("|", "/", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
			.Trim();
	}

	private static string Limit(string value, int maxChars)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
		{
			return value ?? string.Empty;
		}
		return value.Substring(0, maxChars) + "...";
	}
}
