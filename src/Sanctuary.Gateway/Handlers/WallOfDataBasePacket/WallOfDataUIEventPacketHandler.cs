using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Game;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class WallOfDataUIEventPacketHandler
{
	private readonly struct RecentTcgSelection
	{
		public readonly DateTime SelectedAtUtc;

		public readonly int ActivityCategoryId;

		public RecentTcgSelection(DateTime selectedAtUtc, int activityCategoryId)
		{
			SelectedAtUtc = selectedAtUtc;
			ActivityCategoryId = activityCategoryId;
		}
	}

	private readonly struct RecentGenericMinigameSelection
	{
		public readonly DateTime SelectedAtUtc;

		public readonly int RequestedCategoryId;

		public readonly int ActivityCategoryId;

		public readonly int SelectedRowId;

		public RecentGenericMinigameSelection(DateTime selectedAtUtc, int requestedCategoryId, int activityCategoryId, int selectedRowId)
		{
			SelectedAtUtc = selectedAtUtc;
			RequestedCategoryId = requestedCategoryId;
			ActivityCategoryId = activityCategoryId;
			SelectedRowId = selectedRowId;
		}
	}

	private static readonly ConcurrentDictionary<ulong, RecentTcgSelection> RecentTcgSelections = new ConcurrentDictionary<ulong, RecentTcgSelection>();

	private static readonly ConcurrentDictionary<ulong, RecentGenericMinigameSelection> RecentGenericMinigameSelections = new ConcurrentDictionary<ulong, RecentGenericMinigameSelection>();

	private static readonly TimeSpan ShowGamesSelectionContinuationWindow = TimeSpan.FromSeconds(2.0);

	private static readonly TimeSpan GenericPlaySelectionWindow = TimeSpan.FromSeconds(60.0);

	private static ILogger _logger;

	private static IResourceManager _resourceManager;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("WallOfDataUIEventPacketHandler");
		_resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static int ClearForStartup()
	{
		int count = RecentTcgSelections.Count;
		RecentTcgSelections.Clear();
		int result = count + RecentGenericMinigameSelections.Count;
		RecentGenericMinigameSelections.Clear();
		return result;
	}

	public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data, bool worldTunnel)
	{
		ulong num = connection.Player?.Guid ?? 0;
		if (!TryDecodeUiEvent(data, out var target, out var action, out var argument))
		{
			_logger.LogWarning("Failed to decode WallOfData UI event. Data={data}", Convert.ToHexString(data));
			MinigameDiagnosticsLog.Warn("WallOfDataUiEventDecodeFailed", "WallOfDataUIEventPacketHandler", num, worldTunnel, MinigameDiagnosticsLog.HexField("payload", data));
			return true;
		}
		int activityCategoryId;
		bool flag = TryGetTcgSelectionCategory(target, action, argument, out activityCategoryId);
		int activityCategoryId2;
		bool flag2 = TryGetBrowserV2SelectionCategory(target, action, argument, out activityCategoryId2) && !flag;
		bool flag3 = IsBrowserV2ShowGames(target, action);
		double selectionAgeMs = -1.0;
		int activityCategoryId3 = 0;
		bool flag4 = flag3 && IsRecentTcgSelection(num, out selectionAgeMs, out activityCategoryId3);
		double selectionAgeMs2 = -1.0;
		int requestedCategoryId = 0;
		int activityCategoryId4 = 0;
		int selectedRowId = 0;
		bool flag5 = flag3 && !flag4 && IsRecentGenericMinigameSelection(num, out selectionAgeMs2, out requestedCategoryId, out activityCategoryId4, out selectedRowId);
		bool flag6 = flag || flag4;
		bool flag7 = flag2 || flag5;
		int num2 = (flag ? activityCategoryId : (flag4 ? activityCategoryId3 : 0));
		int num3 = (flag2 ? activityCategoryId2 : (flag5 ? requestedCategoryId : 0));
		int activityCategoryId5 = (flag5 ? activityCategoryId4 : num3);
		PacketReadyActivityDefinitionRow row = null;
		if (flag7)
		{
			PacketReadyActivityDefinitions.TryGetPrimaryCategoryRow(num3, num, worldTunnel, out activityCategoryId5, out row);
		}
		string value = (flag4 ? ("BrowserV2:ShowGamesAfterSelect(" + num2 + ")") : (flag ? ("BrowserV2:SelectGame(" + num2 + ")") : (flag5 ? ("BrowserV2:ShowGamesAfterSelect(" + activityCategoryId5 + ")") : (flag2 ? ("BrowserV2:SelectGame(" + num3 + ")") : ("BrowserV2:" + action)))));
		_logger.LogInformation("WallOfData UI event. Target={target}, Action={action}, Argument={argument}, Player={player}, WorldTunnel={worldTunnel}, TcgSelectionContinuation={tcgContinuation}, TcgSelectionAgeMs={tcgSelectionAgeMs}, GenericSelectionContinuation={genericContinuation}, GenericSelectionAgeMs={genericSelectionAgeMs}", target, action, argument, num, worldTunnel, flag4, selectionAgeMs, flag5, selectionAgeMs2);
		MinigameDiagnosticsLog.Info("WallOfDataUiEventObserved", "WallOfDataUIEventPacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("target", target), MinigameDiagnosticsLog.Field("action", action), MinigameDiagnosticsLog.Field("argument", argument), MinigameDiagnosticsLog.Field("selectedId", flag6 ? 727 : (row?.Id ?? 0)), MinigameDiagnosticsLog.Field("activityId", flag6 ? 24 : (row?.Id ?? 0)), MinigameDiagnosticsLog.Field("gameKey", flag6 ? "tcg" : (flag7 ? ("activity-category-" + activityCategoryId5) : string.Empty)), MinigameDiagnosticsLog.Field("gameName", flag6 ? "Trading Card Game" : ((row != null) ? ("NameId:" + row.NameId) : string.Empty)), MinigameDiagnosticsLog.Field("categoryId", flag6 ? num2 : (flag7 ? activityCategoryId5 : 0)), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", flag6 ? 23 : (row?.Category ?? 0)), MinigameDiagnosticsLog.Field("miniGameId", flag6 ? 727 : (row?.Unknown3 ?? 0)), MinigameDiagnosticsLog.Field("handler", "WallOfDataUIEventPacketHandler"), MinigameDiagnosticsLog.Field("sourceEvent", value), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", flag3 && !flag4 && !flag5), MinigameDiagnosticsLog.Field("selectionContinuation", flag4 || flag5), MinigameDiagnosticsLog.Field("selectionAgeMs", flag5 ? selectionAgeMs2 : selectionAgeMs), MinigameDiagnosticsLog.HexField("payload", data));
		if (TryParsePlayButtonClicked(target, action, argument, out var playGuid, out var source))
		{
			if (TryHandleGenericPlayButtonClicked(connection, worldTunnel, num, playGuid, source, argument, data))
			{
				return true;
			}
			DateTime utcNow = DateTime.UtcNow;
			(int DisplayRowId, string Title, string Description) tuple = MiniGamePacketHandler.ResolveTradingCardStartScreenDisplayForDiagnostics(playGuid);
			int item = tuple.DisplayRowId;
			string item2 = tuple.Title;
			string item3 = tuple.Description;
			int item4 = MiniGamePacketHandler.GetTradingCardStartScreenHeaderForDiagnostics().StateId;
			string value2 = item2;
			TcgMatchmakingState.TryGetRecentTcgTileSelection(num, out var selectionAgeMs3, out var activityCategoryId6);
			bool flag8 = MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(playGuid);
			if (flag8)
			{
				TcgMatchmakingState.MarkTcgDetailPlaySelected(num, playGuid);
			}
			MinigameDiagnosticsLog.Info("PlayClicked", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFieldsForCategory(727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", "WallOfData:" + source, false, activityCategoryId6, MinigameDiagnosticsLog.Field("playGuid", playGuid), MinigameDiagnosticsLog.Field("playButtonClickedRawArgument", argument), MinigameDiagnosticsLog.Field("selectedRowId", playGuid), MinigameDiagnosticsLog.Field("selectedRowStoredInTcgMatchmakingState", flag8), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", flag8 ? utcNow.ToString("O") : string.Empty), MinigameDiagnosticsLog.Field("selectedRowTitle", value2), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("startScreenStateId", item4), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", item), MinigameDiagnosticsLog.Field("currentCategoryId", activityCategoryId6), MinigameDiagnosticsLog.Field("selectionAgeMs", selectionAgeMs3), MinigameDiagnosticsLog.Field("expectedNativeTcgMinigameId", 727), MinigameDiagnosticsLog.Field("targetStartScreen", "MinigameStartHandler:setNotReady,MinigameStartHandler:show"), MinigameDiagnosticsLog.Field("target", target), MinigameDiagnosticsLog.Field("action", action), MinigameDiagnosticsLog.Field("argument", argument), MinigameDiagnosticsLog.Field("startScreenBootstrapDeferredToActivityService", true), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("note", "Exact Flash PlayButtonClicked event surfaced through WallOfData. This records the detail row; ActivityService keeps the working native start-screen opener path."), MinigameDiagnosticsLog.HexField("payload", data)));
			MinigameDiagnosticsLog.Info("TcgPlaySelectedDetailRow", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFieldsForCategory(727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", "WallOfData:" + source, false, activityCategoryId6, MinigameDiagnosticsLog.Field("playButtonClickedRawArgument", argument), MinigameDiagnosticsLog.Field("selectedRowId", playGuid), MinigameDiagnosticsLog.Field("selectedRowStoredInTcgMatchmakingState", flag8), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", flag8 ? utcNow.ToString("O") : string.Empty), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("startScreenStateId", item4), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", item), MinigameDiagnosticsLog.Field("knownStartScreenDisplayRow", flag8), MinigameDiagnosticsLog.Field("currentCategoryId", activityCategoryId6), MinigameDiagnosticsLog.Field("selectionAgeMs", selectionAgeMs3), MinigameDiagnosticsLog.Field("startScreenBootstrapDeferredToActivityService", true), MinigameDiagnosticsLog.Field("didLaunch", false)));
		}
		if (flag3)
		{
			if (flag4)
			{
				MinigameDiagnosticsLog.Info("GameDetailPanelUpdated", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFieldsForCategory(727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", "BrowserV2:ShowGamesAfterSelect(" + num2 + ")", false, num2, MinigameDiagnosticsLog.Field("selectionContinuation", true), MinigameDiagnosticsLog.Field("selectionAgeMs", selectionAgeMs), MinigameDiagnosticsLog.Field("rootCauseCandidate", "BrowserV2 sends ShowGames shortly after SelectGame; treating that continuation as a fresh menu open resets the visible detail panel to No Game Selected."), MinigameDiagnosticsLog.Field("fixCandidate", "Preserve the TCG selection category for the short ShowGames-after-select bridge event and send the native TCG detail bridge instead of the no-selection preload.")));
				if (PacketReadyActivityDefinitions.IsStartupListEnabled())
				{
					LogOldTcgOverrideSuppressed(num, worldTunnel, "TcgDetailUiPatch.SendDatasourceAndPopulateAfterDatasourceDelay", "BrowserV2:ShowGamesAfterSelect(" + num2 + ")", num2);
					PacketReadyActivityDefinitions.TryShowCategoryDetailFromStartupList(connection, worldTunnel, num, 11, "browser-v2-show-games-after-select-packet-ready-" + num2);
				}
				else
				{
					TcgDetailUiPatch.SendDatasourceAndPopulateAfterDatasourceDelay(connection, worldTunnel, num, "browser-v2-show-games-after-select-" + num2, num2);
				}
				return true;
			}
			if (flag5)
			{
				MinigameDiagnosticsLog.Info("GameDetailPanelUpdated", "WallOfDataUIEventPacketHandler", num, worldTunnel, GenericUiFields(row, selectedRowId, activityCategoryId4, "BrowserV2:ShowGamesAfterSelect(" + activityCategoryId4 + ")", false, MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId), MinigameDiagnosticsLog.Field("selectionContinuation", true), MinigameDiagnosticsLog.Field("selectionAgeMs", selectionAgeMs2), MinigameDiagnosticsLog.Field("rootCauseCandidate", "BrowserV2 sends ShowGames shortly after SelectGame; treating that continuation as a fresh menu open resets the visible detail panel to No Game Selected."), MinigameDiagnosticsLog.Field("fixCandidate", "Preserve the selected non-TCG activity category and repopulate MinigameDetail from packet-ready ClientActivityDefinitions.")));
				PacketReadyActivityDefinitions.TrySendCategoryDetail(connection, worldTunnel, num, requestedCategoryId, "browser-v2-show-games-after-select-" + activityCategoryId4);
				return true;
			}
			MinigameDiagnosticsLog.Info("GamesMenuOpened", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFields(0, 0, string.Empty, string.Empty, string.Empty, "BrowserV2:ShowGames", true));
			MinigameDiagnosticsLog.Info("NoGameSelectedShown", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFields(0, 0, string.Empty, string.Empty, string.Empty, "BrowserV2:ShowGames", true));
			if (PacketReadyActivityDefinitions.IsStartupListEnabled())
			{
				LogOldTcgOverrideSuppressed(num, worldTunnel, "TcgDetailUiPatch.SendWarmDatasourcePreload", "BrowserV2:ShowGames", 11);
			}
			else
			{
				TcgDetailUiPatch.SendWarmDatasourcePreload(connection, worldTunnel, num, "browser-v2-show-games-warm-preload", "BrowserV2:ShowGames");
			}
			TcgDetailUiPatch.LogOuterGamesGridDiagnostics(num, worldTunnel, "BrowserV2:ShowGames");
			return true;
		}
		if (flag)
		{
			MarkTcgSelection(num, num2);
			TcgMatchmakingState.MarkTcgTileSelected(num, num2);
			MinigameDiagnosticsLog.Info("GameTileClicked", "WallOfDataUIEventPacketHandler", num, worldTunnel, TcgUiFieldsForCategory(727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", "BrowserV2:SelectGame(" + num2 + ")", false, num2));
			if (PacketReadyActivityDefinitions.IsStartupListEnabled())
			{
				LogOldTcgOverrideSuppressed(num, worldTunnel, "TcgDetailUiPatch.SendDatasourceAndPopulateAfterDatasourceDelay", "BrowserV2:SelectGame(" + num2 + ")", num2);
				PacketReadyActivityDefinitions.TryShowCategoryDetailFromStartupList(connection, worldTunnel, num, 11, "browser-v2-select-game-packet-ready-" + num2);
			}
			else
			{
				TcgDetailUiPatch.SendDatasourceAndPopulateAfterDatasourceDelay(connection, worldTunnel, num, "browser-v2-select-game-" + num2, num2);
			}
			return true;
		}
		if (flag2)
		{
			MarkGenericMinigameSelection(num, activityCategoryId2, activityCategoryId5, row?.Id ?? 0);
			MinigameDiagnosticsLog.Info("GameTileClicked", "WallOfDataUIEventPacketHandler", num, worldTunnel, GenericUiFields(row, row?.Id ?? activityCategoryId2, activityCategoryId5, "BrowserV2:SelectGame(" + activityCategoryId2 + ")", false, MinigameDiagnosticsLog.Field("requestedCategoryId", activityCategoryId2)));
			if (!PacketReadyActivityDefinitions.TrySendCategoryDetail(connection, worldTunnel, num, activityCategoryId2, "browser-v2-select-game-" + activityCategoryId2))
			{
				MinigameDiagnosticsLog.Warn("GenericMinigameSelectGameBridgeFailed", "WallOfDataUIEventPacketHandler", num, worldTunnel, GenericUiFields(row, row?.Id ?? activityCategoryId2, activityCategoryId5, "BrowserV2:SelectGame(" + activityCategoryId2 + ")", true, MinigameDiagnosticsLog.Field("requestedCategoryId", activityCategoryId2)));
			}
			return true;
		}
		return true;
	}

	public static bool TryGetRecentGenericMinigameSelectionForPlay(ulong playerGuid, out double selectionAgeMs, out int requestedCategoryId, out int activityCategoryId, out int selectedRowId)
	{
		selectionAgeMs = -1.0;
		requestedCategoryId = 0;
		activityCategoryId = 0;
		selectedRowId = 0;
		if (playerGuid == 0L || !RecentGenericMinigameSelections.TryGetValue(playerGuid, out var value))
		{
			return false;
		}
		TimeSpan timeSpan = DateTime.UtcNow - value.SelectedAtUtc;
		selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
		if (timeSpan <= GenericPlaySelectionWindow)
		{
			requestedCategoryId = value.RequestedCategoryId;
			activityCategoryId = value.ActivityCategoryId;
			selectedRowId = value.SelectedRowId;
			return true;
		}
		RecentGenericMinigameSelections.TryRemove(playerGuid, out var _);
		return false;
	}

	private static void LogOldTcgOverrideSuppressed(ulong playerGuid, bool worldTunnel, string sender, string sourceEvent, int activityCategoryId)
	{
		MinigameDiagnosticsLog.Info("OldTcgOverrideSuppressed", "WallOfDataUIEventPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sender", sender), MinigameDiagnosticsLog.Field("reason", "packet-ready-test"), MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
	}

	private static bool TryDecodeUiEvent(ReadOnlySpan<byte> data, out string target, out string action, out string argument)
	{
		if (TryDecodeUiEventAt(data, 0, out target, out action, out argument))
		{
			return true;
		}
		if (TryDecodeUiEventAt(data, 1, out target, out action, out argument))
		{
			return true;
		}
		if (TryDecodeUiEventAt(data, 3, out target, out action, out argument))
		{
			return true;
		}
		target = string.Empty;
		action = string.Empty;
		argument = string.Empty;
		return false;
	}

	private static bool TryDecodeUiEventAt(ReadOnlySpan<byte> data, int offset, out string target, out string action, out string argument)
	{
		target = string.Empty;
		action = string.Empty;
		argument = string.Empty;
		if (!TryReadString(data, ref offset, out target) || !TryReadString(data, ref offset, out action))
		{
			return false;
		}
		if (offset < data.Length && !TryReadString(data, ref offset, out argument))
		{
			argument = string.Empty;
		}
		if (target.Length > 0)
		{
			return action.Length > 0;
		}
		return false;
	}

	private static bool TryReadString(ReadOnlySpan<byte> data, ref int offset, out string value)
	{
		value = string.Empty;
		if (offset < 0 || offset + 4 > data.Length)
		{
			return false;
		}
		int num = BitConverter.ToInt32(data.Slice(offset, 4));
		offset += 4;
		if (num < 0 || offset + num > data.Length)
		{
			return false;
		}
		value = Encoding.UTF8.GetString(data.Slice(offset, num));
		offset += num;
		return true;
	}

	private static bool IsBrowserV2(string target)
	{
		return string.Equals(target, "BrowserV2", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsBrowserV2ShowGames(string target, string action)
	{
		if (IsBrowserV2(target))
		{
			return string.Equals(action, "ShowGames", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool TryGetBrowserV2SelectionCategory(string target, string action, string argument, out int activityCategoryId)
	{
		activityCategoryId = 0;
		if (IsBrowserV2(target) && string.Equals(action, "SelectGame", StringComparison.OrdinalIgnoreCase) && int.TryParse(argument, out activityCategoryId))
		{
			return activityCategoryId > 0;
		}
		return false;
	}

	private static bool TryGetTcgSelectionCategory(string target, string action, string argument, out int activityCategoryId)
	{
		activityCategoryId = 0;
		if (TryGetBrowserV2SelectionCategory(target, action, argument, out activityCategoryId))
		{
			return IsCanonicalTcgSelectionCategory(activityCategoryId);
		}
		return false;
	}

	private static bool IsCanonicalTcgSelectionCategory(int activityCategoryId)
	{
		if (activityCategoryId != 7 && activityCategoryId != 11 && activityCategoryId != 24 && activityCategoryId != 23)
		{
			return activityCategoryId == 727;
		}
		return true;
	}

	private static bool TryParsePlayButtonClicked(string target, string action, string argument, out int playGuid, out string source)
	{
		if (string.Equals(target, "MiniGameDetail", StringComparison.OrdinalIgnoreCase) && string.Equals(action, "PlayButtonClicked", StringComparison.OrdinalIgnoreCase) && int.TryParse(argument, out playGuid))
		{
			source = "action-argument";
			return true;
		}
		if (TryParsePlayButtonClickedValue(target, out playGuid))
		{
			source = "target";
			return true;
		}
		if (TryParsePlayButtonClickedValue(action, out playGuid))
		{
			source = "action";
			return true;
		}
		if (TryParsePlayButtonClickedValue(argument, out playGuid))
		{
			source = "argument";
			return true;
		}
		playGuid = 0;
		source = string.Empty;
		return false;
	}

	private static bool TryParsePlayButtonClickedValue(string value, out int playGuid)
	{
		playGuid = 0;
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		int num = value.IndexOf("PlayButtonClicked(", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return false;
		}
		num += "PlayButtonClicked(".Length;
		int num2 = value.IndexOf(')', num);
		if (num2 < 0)
		{
			num2 = value.Length;
		}
		return int.TryParse(value.Substring(num, num2 - num).Trim(), out playGuid);
	}

	private static bool TryHandleGenericPlayButtonClicked(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int playGuid, string playSource, string rawArgument, ReadOnlySpan<byte> payload)
	{
		if (MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(playGuid) || TcgSessionRegistry.IsTcgLaunchActivityId(playGuid) || IsCanonicalTcgSelectionCategory(playGuid))
		{
			return false;
		}
		if (!PacketReadyActivityDefinitions.TryGetRowByActivityId(playGuid, playerGuid, worldTunnel, out var row) || row.Category == 11)
		{
			return false;
		}
		MarkGenericMinigameSelection(playerGuid, row.Category, row.Category, row.Id);
		MinigameDiagnosticsLog.Info("GenericMinigamePlaySelectedDetailRow", "WallOfDataUIEventPacketHandler", playerGuid, worldTunnel, GenericUiFields(row, row.Id, row.Category, "WallOfData:" + playSource, false, MinigameDiagnosticsLog.Field("playGuid", playGuid), MinigameDiagnosticsLog.Field("playButtonClickedRawArgument", rawArgument), MinigameDiagnosticsLog.Field("selectedRowId", row.Id), MinigameDiagnosticsLog.Field("selectedRowStoredInGenericSelection", true), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", DateTime.UtcNow.ToString("O")), MinigameDiagnosticsLog.Field("startScreenBootstrapDeferredToActivityService", true), MinigameDiagnosticsLog.HexField("payload", payload)));
		return true;
	}

	private static string GetTcgDetailRowTitle(int rowId)
	{
		return rowId switch
		{
			41 => "Free Realms Trading Card Game Lobby", 
			603 => "Bry's Tournament Deck", 
			604 => "Bry's Trick Deck", 
			602 => "Free Realms Trading Card Game Tutorial", 
			499 => "Poe's Tournament Deck", 
			507 => "Poe's Tournament Deck", 
			_ => string.Empty, 
		};
	}

	private static void MarkTcgSelection(ulong playerGuid, int activityCategoryId)
	{
		if (playerGuid != 0L)
		{
			RecentTcgSelections[playerGuid] = new RecentTcgSelection(DateTime.UtcNow, activityCategoryId);
		}
	}

	private static void MarkGenericMinigameSelection(ulong playerGuid, int requestedCategoryId, int activityCategoryId, int selectedRowId)
	{
		if (playerGuid != 0L)
		{
			RecentGenericMinigameSelections[playerGuid] = new RecentGenericMinigameSelection(DateTime.UtcNow, requestedCategoryId, activityCategoryId, selectedRowId);
		}
	}

	private static bool IsRecentTcgSelection(ulong playerGuid, out double selectionAgeMs, out int activityCategoryId)
	{
		selectionAgeMs = -1.0;
		activityCategoryId = 0;
		if (playerGuid == 0L || !RecentTcgSelections.TryGetValue(playerGuid, out var value))
		{
			return false;
		}
		TimeSpan timeSpan = DateTime.UtcNow - value.SelectedAtUtc;
		selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
		if (timeSpan <= ShowGamesSelectionContinuationWindow)
		{
			activityCategoryId = value.ActivityCategoryId;
			return true;
		}
		RecentTcgSelections.TryRemove(playerGuid, out var _);
		return false;
	}

	private static bool IsRecentGenericMinigameSelection(ulong playerGuid, out double selectionAgeMs, out int requestedCategoryId, out int activityCategoryId, out int selectedRowId)
	{
		selectionAgeMs = -1.0;
		requestedCategoryId = 0;
		activityCategoryId = 0;
		selectedRowId = 0;
		if (playerGuid == 0L || !RecentGenericMinigameSelections.TryGetValue(playerGuid, out var value))
		{
			return false;
		}
		TimeSpan timeSpan = DateTime.UtcNow - value.SelectedAtUtc;
		selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
		if (timeSpan <= ShowGamesSelectionContinuationWindow)
		{
			requestedCategoryId = value.RequestedCategoryId;
			activityCategoryId = value.ActivityCategoryId;
			selectedRowId = value.SelectedRowId;
			return true;
		}
		RecentGenericMinigameSelections.TryRemove(playerGuid, out var _);
		return false;
	}

	private static string[] TcgUiFields(int selectedId, int activityId, string gameKey, string gameName, string handler, string sourceEvent, bool didShowNoGameSelected, params string[] extraFields)
	{
		return TcgUiFieldsForCategory(selectedId, activityId, gameKey, gameName, handler, sourceEvent, didShowNoGameSelected, 11, extraFields);
	}

	private static string[] TcgUiFieldsForCategory(int selectedId, int activityId, string gameKey, string gameName, string handler, string sourceEvent, bool didShowNoGameSelected, int activityCategoryId, params string[] extraFields)
	{
		string[] array = new string[14 + extraFields.Length];
		array[0] = MinigameDiagnosticsLog.Field("selectedGame", (selectedId > 0) ? gameName : string.Empty);
		array[1] = MinigameDiagnosticsLog.Field("selectedId", selectedId);
		array[2] = MinigameDiagnosticsLog.Field("activityId", activityId);
		array[3] = MinigameDiagnosticsLog.Field("gameKey", gameKey);
		array[4] = MinigameDiagnosticsLog.Field("gameName", gameName);
		array[5] = MinigameDiagnosticsLog.Field("categoryId", (selectedId > 0) ? activityCategoryId : 0);
		array[6] = MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", (selectedId > 0) ? 23 : 0);
		array[7] = MinigameDiagnosticsLog.Field("miniGameId", selectedId);
		array[8] = MinigameDiagnosticsLog.Field("handler", handler);
		array[9] = MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent);
		array[10] = MinigameDiagnosticsLog.Field("didLaunch", false);
		array[11] = MinigameDiagnosticsLog.Field("didSendInvite", false);
		array[12] = MinigameDiagnosticsLog.Field("didShowNoGameSelected", didShowNoGameSelected);
		array[13] = MinigameDiagnosticsLog.Field("nativeEvent", true);
		for (int i = 0; i < extraFields.Length; i++)
		{
			array[14 + i] = extraFields[i];
		}
		return array;
	}

	private static string[] GenericUiFields(PacketReadyActivityDefinitionRow row, int selectedId, int activityCategoryId, string sourceEvent, bool didShowNoGameSelected, params string[] extraFields)
	{
		string value = ((row != null) ? ("NameId:" + row.NameId) : string.Empty);
		int num = row?.Id ?? selectedId;
		int num2 = row?.Id ?? selectedId;
		string[] array = new string[14 + extraFields.Length];
		array[0] = MinigameDiagnosticsLog.Field("selectedGame", value);
		array[1] = MinigameDiagnosticsLog.Field("selectedId", num);
		array[2] = MinigameDiagnosticsLog.Field("activityId", num2);
		array[3] = MinigameDiagnosticsLog.Field("gameKey", (activityCategoryId > 0) ? ("activity-category-" + activityCategoryId) : string.Empty);
		array[4] = MinigameDiagnosticsLog.Field("gameName", value);
		array[5] = MinigameDiagnosticsLog.Field("categoryId", activityCategoryId);
		array[6] = MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", row?.Category ?? 0);
		array[7] = MinigameDiagnosticsLog.Field("miniGameId", row?.Unknown3 ?? 0);
		array[8] = MinigameDiagnosticsLog.Field("handler", "PacketReadyActivityDefinitions");
		array[9] = MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent);
		array[10] = MinigameDiagnosticsLog.Field("didLaunch", false);
		array[11] = MinigameDiagnosticsLog.Field("didSendInvite", false);
		array[12] = MinigameDiagnosticsLog.Field("didShowNoGameSelected", didShowNoGameSelected);
		array[13] = MinigameDiagnosticsLog.Field("nativeEvent", true);
		for (int i = 0; i < extraFields.Length; i++)
		{
			array[14 + i] = extraFields[i];
		}
		return array;
	}
}
