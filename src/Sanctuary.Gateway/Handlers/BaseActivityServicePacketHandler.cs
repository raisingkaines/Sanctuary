using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseActivityServicePacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BaseActivityServicePacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, ReadOnlySpan<byte> rawPayload, bool worldTunnel)
	{
		ulong playerGuid = connection.Player?.Guid ?? 0;
		if (!reader.TryRead(out byte result))
		{
			_logger.LogError("Failed to read activity-service family.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", "ActivityService"), MinigameDiagnosticsLog.Field("reason", "missing family"), MinigameDiagnosticsLog.HexField("payload", rawPayload));
			return false;
		}
		if (!reader.TryRead(out byte result2))
		{
			_logger.LogError("Failed to read activity-service packet type.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", result), MinigameDiagnosticsLog.Field("reason", "missing packet type"), MinigameDiagnosticsLog.HexField("payload", rawPayload));
			return false;
		}
		MinigameDiagnosticsLog.Info("packet-received", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", "ActivityService"), MinigameDiagnosticsLog.Field("activityFamily", result), MinigameDiagnosticsLog.Field("type", result2), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 167, "BaseActivityServicePacketHandler", "ActivityService", result2, reader.RemainingSpan, MinigameDiagnosticsLog.Field("activityFamily", result));
		if (result == 1 && result2 == 2)
		{
			return HandleJoinActivityRequest(connection, reader, worldTunnel, playerGuid);
		}
		if (result == 1 && (result2 == 1 || result2 == 4 || result2 == 5))
		{
			MinigameDiagnosticsLog.Info("activity-service-detail-request-observed", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activityFamily", result), MinigameDiagnosticsLog.Field("type", result2), MinigameDiagnosticsLog.Field("reason", "native-resource-datasource-path"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
			return true;
		}
		if (result == 2 && result2 == 1)
		{
			MinigameDiagnosticsLog.Info("activity-service-scheduled-list-observed", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activityFamily", result), MinigameDiagnosticsLog.Field("type", result2), MinigameDiagnosticsLog.Field("reason", "native-resource-datasource-path"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
			return true;
		}
		MinigameDiagnosticsLog.Warn("unhandled-packet", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", "ActivityService"), MinigameDiagnosticsLog.Field("activityFamily", result), MinigameDiagnosticsLog.Field("type", result2), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		return true;
	}

	private static bool HandleJoinActivityRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		int result = 0;
		reader.TryRead(out result);
		MinigameDiagnosticsLog.Info("join-activity-request", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		if (result == 0)
		{
			if (GenericMinigameStartScreenBridge.TryHandleJoinActivityRequest(connection, worldTunnel, playerGuid, result, reader.RemainingSpan))
			{
				return true;
			}
			MinigameDiagnosticsLog.Info("NoGameSelectedShown", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedGame", string.Empty), MinigameDiagnosticsLog.Field("selectedId", 0), MinigameDiagnosticsLog.Field("activityId", 0), MinigameDiagnosticsLog.Field("gameKey", string.Empty), MinigameDiagnosticsLog.Field("gameName", string.Empty), MinigameDiagnosticsLog.Field("categoryId", 0), MinigameDiagnosticsLog.Field("miniGameId", 0), MinigameDiagnosticsLog.Field("handler", string.Empty), MinigameDiagnosticsLog.Field("sourceEvent", "JoinActivityRequest"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", true), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
			return true;
		}
		bool num = TcgSessionRegistry.IsTcgLaunchActivityId(result);
		PacketReadyActivityDefinitionRow activityRow;
		bool flag = TryGetPacketReadyTradingCardActivityRow(result, playerGuid, worldTunnel, out activityRow);
		if (num || flag)
		{
			LogPlayClickedFromActivityService(playerGuid, worldTunnel, result, !MiniGamePacketHandler.TCG_START_SCREEN_ONLY, reader.RemainingSpan);
			if (MiniGamePacketHandler.TCG_START_SCREEN_ONLY)
			{
				string selectedRowSource;
				bool usedRecentDetailSelection;
				bool fallbackUsed;
				bool stateHasSelection;
				bool stateSelectionFresh;
				int storedSelectedRowId;
				double selectedRowAgeMs;
				DateTime selectedRowStoredUtc;
				int num2 = ResolveStartScreenSelectedRow(playerGuid, result, out selectedRowSource, out usedRecentDetailSelection, out fallbackUsed, out stateHasSelection, out stateSelectionFresh, out storedSelectedRowId, out selectedRowAgeMs, out selectedRowStoredUtc);
				(int DisplayRowId, string Title, string Description) tuple = MiniGamePacketHandler.ResolveTradingCardStartScreenDisplayForDiagnostics(num2);
				int item = tuple.DisplayRowId;
				string item2 = tuple.Title;
				string item3 = tuple.Description;
				int item4 = MiniGamePacketHandler.GetTradingCardStartScreenHeaderForDiagnostics().StateId;
				int num3 = result;
				string text = TcgSessionRegistry.Register(playerGuid, num3);
				TcgMatchmakingState.MarkLaunchSent(playerGuid, text);
				MinigameDiagnosticsLog.Info("TcgActivityServiceStartScreenSelectedRow", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("requestedActivity", result), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedRowSource", selectedRowSource), MinigameDiagnosticsLog.Field("selectedRowFallbackUsed", fallbackUsed), MinigameDiagnosticsLog.Field("selectedRowStoredInTcgMatchmakingState", usedRecentDetailSelection), MinigameDiagnosticsLog.Field("tcgMatchmakingStateHasSelection", stateHasSelection), MinigameDiagnosticsLog.Field("tcgMatchmakingStateSelectedRowId", storedSelectedRowId), MinigameDiagnosticsLog.Field("tcgMatchmakingStateSelectionFresh", stateSelectionFresh), MinigameDiagnosticsLog.Field("tcgMatchmakingStateExpiredBeforeActivityService", stateHasSelection && !stateSelectionFresh), MinigameDiagnosticsLog.Field("tcgMatchmakingStateSelectionAgeMs", selectedRowAgeMs), MinigameDiagnosticsLog.Field("tcgMatchmakingStateSelectedAtUtc", (selectedRowStoredUtc == DateTime.MinValue) ? string.Empty : selectedRowStoredUtc.ToString("O")), MinigameDiagnosticsLog.Field("storedSelectedRowId", storedSelectedRowId), MinigameDiagnosticsLog.Field("selectedRowAgeMs", selectedRowAgeMs), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", (selectedRowStoredUtc == DateTime.MinValue) ? string.Empty : selectedRowStoredUtc.ToString("O")), MinigameDiagnosticsLog.Field("resolvedStartScreenDisplayRowId", item), MinigameDiagnosticsLog.Field("startScreenStateId", num3), MinigameDiagnosticsLog.Field("legacyHeaderStateId", item4), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("packetReadyTcgActivity", flag), MinigameDiagnosticsLog.Field("packetReadyTcgRowId", activityRow?.Id ?? 0), MinigameDiagnosticsLog.Field("packetReadyTcgCategoryId", activityRow?.Category ?? 0), MinigameDiagnosticsLog.Field("packetReadyTcgNameId", activityRow?.NameId ?? 0), MinigameDiagnosticsLog.Field("packetReadyTcgDescriptionId", activityRow?.DescriptionId ?? 0), MinigameDiagnosticsLog.Field("ticketSessionTypePreserved", num3), MinigameDiagnosticsLog.Field("miniGameInfoNameTokenExpectedToVary", true), MinigameDiagnosticsLog.Field("miniGameInfoDescriptionTokenExpectedToVary", true), MinigameDiagnosticsLog.Field("envelopeFrozen", true), MinigameDiagnosticsLog.Field("didLaunch", false));
				MinigameDiagnosticsLog.Info("TcgStartScreenJoinActivityMiniGameInfoPath", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", result), MinigameDiagnosticsLog.Field("ticket", text), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("startScreenStateId", item4), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", item), MinigameDiagnosticsLog.Field("nativeClientEvent", "ActivityPacketJoinActivityRequest"), MinigameDiagnosticsLog.Field("response", "controlled MiniGame:JoinGame/subtype16 start-screen opener with trailing native param_4 flag=1"), MinigameDiagnosticsLog.Field("suppressedPackets", "ClientActivityLaunch session-ticket path,RequestStartGame/subtype5 response,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
				(int, int, int, int, int, string, string) tuple2 = MiniGamePacketHandler.ResolveTradingCardStartScreenLaunchMetadataForDiagnostics(num2);
				GenericMinigameStartScreenBridge.SendTradingCardStartScreenActivityLaunchPrelude(connection, worldTunnel, playerGuid, result, num2, tuple2.Item1, tuple2.Item2, tuple2.Item3, tuple2.Item4, tuple2.Item5, tuple2.Item6, tuple2.Item7, reader.RemainingSpan);
				MiniGamePacketHandler.TrackTradingCardStartScreenOpenedFromActivityLaunchPrelude(connection, worldTunnel, playerGuid, result, num2, "ActivityServiceJoinActivityRequest:sanctuary-prelude-only");
				return true;
			}
			ClientActivityLaunchPacketHandler.CompleteTcgLaunch(connection, worldTunnel, playerGuid);
			return true;
		}
		if (IsTcgCarouselAliasId(result))
		{
			LogPlayClickedFromActivityService(playerGuid, worldTunnel, result, willLaunch: false, reader.RemainingSpan);
		}
		if (GenericMinigameStartScreenBridge.TryHandleJoinActivityRequest(connection, worldTunnel, playerGuid, result, reader.RemainingSpan))
		{
			return true;
		}
		MinigameDiagnosticsLog.Warn("join-activity-ignored", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		return true;
	}

	private static bool TryGetPacketReadyTradingCardActivityRow(int requestedActivity, ulong playerGuid, bool worldTunnel, out PacketReadyActivityDefinitionRow activityRow)
	{
		activityRow = null;
		if (requestedActivity > 0 && PacketReadyActivityDefinitions.IsStartupListEnabled() && PacketReadyActivityDefinitions.TryGetRowByActivityId(requestedActivity, playerGuid, worldTunnel, out activityRow))
		{
			if (activityRow.Id != 7)
			{
				return activityRow.Category == 11;
			}
			return true;
		}
		return false;
	}

	private static int ResolveStartScreenSelectedRow(ulong playerGuid, int requestedActivity, out string selectedRowSource, out bool usedRecentDetailSelection, out bool fallbackUsed, out bool stateHasSelection, out bool stateSelectionFresh, out int storedSelectedRowId, out double selectedRowAgeMs, out DateTime selectedRowStoredUtc)
	{
		stateHasSelection = TcgMatchmakingState.TryGetTcgDetailPlaySelectionForDiagnostics(playerGuid, out storedSelectedRowId, out selectedRowAgeMs, out selectedRowStoredUtc, out stateSelectionFresh);
		usedRecentDetailSelection = stateHasSelection & stateSelectionFresh;
		fallbackUsed = false;
		if (usedRecentDetailSelection)
		{
			selectedRowSource = "recent PlayButtonClicked";
			return storedSelectedRowId;
		}
		if (MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(requestedActivity))
		{
			selectedRowSource = "requested activity";
			return requestedActivity;
		}
		selectedRowSource = (stateHasSelection ? "fallback: expired PlayButtonClicked and requested activity is not a display row" : "fallback: no PlayButtonClicked state and requested activity is not a display row");
		fallbackUsed = true;
		return 41;
	}

	private static bool IsTcgCarouselAliasId(int activityId)
	{
		if (activityId != 36)
		{
			if (activityId >= 611)
			{
				return activityId <= 615;
			}
			return false;
		}
		return true;
	}

	private static void LogPlayClickedFromActivityService(ulong playerGuid, bool worldTunnel, int requestedActivity, bool willLaunch, ReadOnlySpan<byte> remaining)
	{
		MinigameDiagnosticsLog.Info("PlayClicked", "BaseActivityServicePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", requestedActivity), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "ActivityServiceJoinActivityRequest"), MinigameDiagnosticsLog.Field("didLaunch", willLaunch), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("requestedActivity", requestedActivity), MinigameDiagnosticsLog.Field("isNativeActivityId", requestedActivity == 7), MinigameDiagnosticsLog.Field("isCarouselRowId", requestedActivity == 39 || requestedActivity == 41 || requestedActivity == 602 || requestedActivity == 603 || requestedActivity == 604), MinigameDiagnosticsLog.Field("isCarouselAliasId", IsTcgCarouselAliasId(requestedActivity)), MinigameDiagnosticsLog.Field("expectedTreasureWarRowId", 39), MinigameDiagnosticsLog.Field("expectedNativeTcgMinigameId", 387), MinigameDiagnosticsLog.HexField("remaining", remaining));
	}
}
