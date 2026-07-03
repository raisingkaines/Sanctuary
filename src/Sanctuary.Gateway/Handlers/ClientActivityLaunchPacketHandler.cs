using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientActivityLaunchPacketHandler
{
	private const int NativeTcgMaxStartupNameLength = 16;

	private const int MinigameActivityLaunchProcessorHash = 526123817;

	private const int TradingCardLaunchActivityId = 7;

	private const int TradingCardLaunchCategoryId = 11;

	private const int TradingCardNativeMiniGameId = 387;

	private const int TradingCardStartScreenInstanceId = 7;

	private static readonly (string Script, int[] Args)[] TradingCardNativeEntryScripts = new(string, int[])[3]
	{
		("MinigameDetail:Show", new int[1] { 11 }),
		("MinigameDetail:Populate", new int[1] { 11 }),
		("MiniGameStateManager:joinMultiplayerMiniGame", new int[1] { 387 })
	};

	private static readonly string[] TradingCardUiLaunchScripts = new string[6] { "MinigameDetail:Hide", "BrowserV2:Hide", "TradingCardGameHandler:Show", "TradingCardGameHandler:show", "TradingCardGameHandler:initiateGame", "TradingCardGameHandler:gotoPortal" };

	private static readonly string[] TradingCardPostGoLobbyScripts = new string[6] { "MinigameDetail:Hide", "BrowserV2:Hide", "TradingCardGameHandler:Show", "TradingCardGameHandler:show", "TradingCardGameHandler:initiateGame", "TradingCardGameHandler:gotoPortal" };

	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ClientActivityLaunchPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		if (!reader.TryRead(out int result))
		{
			_logger.LogError("Failed to read activity launch packet type.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "ClientActivityLaunchPacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "ClientActivityLaunch"), MinigameDiagnosticsLog.Field("reason", "missing packet type"));
			return false;
		}
		ulong num = connection.Player?.Guid ?? 0;
		_logger.LogInformation("ClientActivityLaunchRequest. Type={type}, Player={player}, WorldTunnel={worldTunnel}, Remaining={remaining}", result, num, worldTunnel, Convert.ToHexString(reader.RemainingSpan));
		MinigameDiagnosticsLog.Info("packet-received", "ClientActivityLaunchPacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("family", "ClientActivityLaunch"), MinigameDiagnosticsLog.Field("type", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 175, "ClientActivityLaunchPacketHandler", "ClientActivityLaunch", result, reader.RemainingSpan);
		return result switch
		{
			14 => SendMatchmakingStartPacket(connection, worldTunnel, num), 
			15 => true, 
			16 => true, 
			17 => HandleInviteResponse(connection, reader, worldTunnel, num), 
			20 => HandleInviteMemberRequest(connection, reader, worldTunnel, num), 
			22 => HandleOwnerLaunchRequest(connection, reader, worldTunnel, num), 
			23 => HandleOwnerMatchmakingRequest(connection, reader, worldTunnel, num), 
			_ => LogUnhandled(connection, result, worldTunnel), 
		};
	}

	public static void SendMatchmakingLaunch(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int queueId, string ticket)
	{
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		using PacketWriter packetWriter = new PacketWriter();
		int launchRequestId = GetLaunchRequestId(playerGuid);
		WriteActivityLaunchBase(packetWriter, 15, launchRequestId, 526123817);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		SendActivityLaunchEvent(connection, playerGuid, "matchmaking-launch-event-bridge", "ActivityEvents:OnMatchmakingLaunch", launchRequestId, worldTunnel);
		_logger.LogDebug("OnMatchmakingLaunch. Player={player}, Queue={queue}", playerGuid, queueId);
		MinigameDiagnosticsLog.Info("matchmaking-launch-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", queueId), MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.Field("launchRequestId", GetLaunchRequestId(playerGuid)), MinigameDiagnosticsLog.Field("processorHash", 526123817), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	public static void CompleteTcgLaunchAfterStartScreenGo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket = null, int selectedRowId = 41)
	{
		if (string.IsNullOrEmpty(ticket))
		{
			ticket = TcgSessionRegistry.Register(playerGuid, 7);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		}
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		string value = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
		string value2 = launchTicket.ToString(CultureInfo.InvariantCulture);
		string launchMemberName = GetLaunchMemberName(connection, playerGuid);
		string reason;
		string text = BuildNativeTcgStartupName(launchMemberName, out reason);
		string value3 = QuoteNativeTcgArgument(text);
		bool hasStoredSelection;
		bool storedSelectionFresh;
		double selectedRowAgeMs;
		DateTime selectedRowStoredUtc;
		(int SelectedRowId, string SelectedTitle, string SelectedDescription) tuple = ResolvePostGoSelectedRow(playerGuid, selectedRowId, out hasStoredSelection, out storedSelectionFresh, out selectedRowAgeMs, out selectedRowStoredUtc);
		int item = tuple.SelectedRowId;
		string item2 = tuple.SelectedTitle;
		string item3 = tuple.SelectedDescription;
		string value4 = "OneTimeSession:SessionResponse,TcgDetailUiPatch:SendDatasourceAndPopulate,MiniGame:ActivityUnlocks,ClientActivityLaunch:InviteDetails,MinigameDetail:Show,MinigameDetail:Populate,MiniGameStateManager:joinMultiplayerMiniGame(387),MinigameDetail:Hide,BrowserV2:Hide,TradingCardGameHandler:Show,TradingCardGameHandler:show,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,ClientActivityLaunch:PlayerAccepted,ClientActivityLaunch:ActivityLaunched,ClientActivityLaunch:MatchmakingStart,ClientActivityLaunch:MatchmakingLaunch,MiniGame:PlayerStartMiniGame,MiniGameCreateGameResultPacket,MiniGame:BeginLoad,InviteStartGameAck,MiniGameStateManager:setSelectedGameId(387),MiniGameStateManager:startGame(387)";
		MinigameDiagnosticsLog.Info("TcgNativeStartupFieldsPrepared", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("registeredSessionId", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("nativeSessionIdAlias", value2), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("realm", "FR"), MinigameDiagnosticsLog.Field("username", launchMemberName), MinigameDiagnosticsLog.Field("nativeStartupUsername", text), MinigameDiagnosticsLog.Field("nativeStartupUsernameArg", value3), MinigameDiagnosticsLog.Field("nativeStartupUsernameAliased", text != launchMemberName), MinigameDiagnosticsLog.Field("nativeStartupUsernameAliasReason", reason), MinigameDiagnosticsLog.Field("namespace", "STATION"), MinigameDiagnosticsLog.Field("host", "127.0.0.1"), MinigameDiagnosticsLog.Field("sanitizedCommand", $"tcg --username={value3} --sessionID={value2} --challenge={launchTicket} --is-founder=false --character={value3} --realm=FR --ticket={launchTicket} --namespace=STATION --host=127.0.0.1 --lang=en-US --country=US"), MinigameDiagnosticsLog.Field("nativeShim", "FreeRealms.exe startup fallback populates realm/sessionID/username before FreeRealmsTCG.dll Initialize"), MinigameDiagnosticsLog.Field("didChangeLaunchOrder", false));
		MinigameDiagnosticsLog.Info("CompleteTcgLaunchAfterStartScreenGo", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "MinigameStartScreen:GO"), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("sessionId", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("selectedRowId", item), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("selectedRowAgeMs", selectedRowAgeMs), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", (selectedRowStoredUtc == DateTime.MinValue) ? string.Empty : selectedRowStoredUtc.ToString("O")), MinigameDiagnosticsLog.Field("selectedRowStoredInTcgMatchmakingState", hasStoredSelection), MinigameDiagnosticsLog.Field("selectedRowFresh", storedSelectionFresh), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("restoredNativeTcgRow", 727), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("restoredActivityId", 24), MinigameDiagnosticsLog.Field("launchCommands", value4), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didGotoPortal", true), MinigameDiagnosticsLog.Field("acceptedLaunchScheduled", false), MinigameDiagnosticsLog.Field("didSendAcceptedLaunchImmediately", false), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didPopulateBrowserV2", false), MinigameDiagnosticsLog.Field("didPopulateMinigameDetail", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgPostGoLaunchIdentity", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("displayRowId", item), MinigameDiagnosticsLog.Field("launchActivityId", 7), MinigameDiagnosticsLog.Field("launchCategoryId", 11), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("restoredNativeTcgRow", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("selectedGameIdSent", 387), MinigameDiagnosticsLog.Field("startGameIdSent", 387), MinigameDiagnosticsLog.Field("createMiniGameIdSent", 387), MinigameDiagnosticsLog.Field("joinMultiplayerMiniGameIdSent", 387), MinigameDiagnosticsLog.Field("gotoPortalSent", true), MinigameDiagnosticsLog.Field("initiateGameSent", true), MinigameDiagnosticsLog.Field("tcgGoMatchmakingSent", false), MinigameDiagnosticsLog.Field("duplicateGuardState", "go guard accepted; single post-GO launch burst only"), MinigameDiagnosticsLog.Field("sourceOfTruth", "last old direct PLAY TCG launch path"));
		SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-post-go-session-response");
		TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		SendTcgInviteSetup(connection, worldTunnel: false, playerGuid, ticket, "post-go-old-direct-launch");
		_logger.LogInformation("Post-GO TCG launch invite staged. Player={player}, WorldTunnel={worldTunnel}, Ticket={ticket}", playerGuid, worldTunnel, ticket);
		MinigameDiagnosticsLog.Info("TcgPostGoOldDirectUiScriptsSent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-old-direct-launch"), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("scripts", value4), MinigameDiagnosticsLog.Field("sourceOfTruth", "last old direct PLAY TCG launch path"), MinigameDiagnosticsLog.Field("acceptedLaunchScheduled", false), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didPopulateBrowserV2", false), MinigameDiagnosticsLog.Field("didPopulateMinigameDetail", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgStartScreenGoLaunchCommandsSent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-old-direct-launch"), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("sessionId", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("selectedRowId", item), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("restoredNativeTcgRow", 727), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("launchCommands", value4), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didGotoPortal", true), MinigameDiagnosticsLog.Field("acceptedLaunchScheduled", false), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didPopulateBrowserV2", false), MinigameDiagnosticsLog.Field("didPopulateMinigameDetail", true));
	}

	public static void PrepareTcgLaunchBehindStartScreenLoading(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, int selectedRowId = 41)
	{
		if (string.IsNullOrEmpty(ticket))
		{
			ticket = TcgSessionRegistry.Register(playerGuid, 7);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		}
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		bool hasStoredSelection;
		bool storedSelectionFresh;
		double selectedRowAgeMs;
		DateTime selectedRowStoredUtc;
		(int SelectedRowId, string SelectedTitle, string SelectedDescription) tuple = ResolvePostGoSelectedRow(playerGuid, selectedRowId, out hasStoredSelection, out storedSelectionFresh, out selectedRowAgeMs, out selectedRowStoredUtc);
		int item = tuple.SelectedRowId;
		string item2 = tuple.SelectedTitle;
		string item3 = tuple.SelectedDescription;
		string value = "OneTimeSession:SessionResponse only; all TCG launch/UI/accepted-launch commands deferred until visible GO click";
		MinigameDiagnosticsLog.Info("TcgPostStartScreenPrepareIdentity", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "start-screen-loading-prep"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("sessionId", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("displayRowId", item), MinigameDiagnosticsLog.Field("selectedRowId", item), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("selectedDescription", item3), MinigameDiagnosticsLog.Field("selectedRowAgeMs", selectedRowAgeMs), MinigameDiagnosticsLog.Field("selectedRowStoredUtc", (selectedRowStoredUtc == DateTime.MinValue) ? string.Empty : selectedRowStoredUtc.ToString("O")), MinigameDiagnosticsLog.Field("selectedRowStoredInTcgMatchmakingState", hasStoredSelection), MinigameDiagnosticsLog.Field("selectedRowFresh", storedSelectionFresh), MinigameDiagnosticsLog.Field("launchActivityId", 7), MinigameDiagnosticsLog.Field("launchCategoryId", 11), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("selectedGameIdSent", 0), MinigameDiagnosticsLog.Field("startGameIdSent", 0), MinigameDiagnosticsLog.Field("joinMultiplayerMiniGameIdSent", 0), MinigameDiagnosticsLog.Field("readinessGate", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("didShowGo", false), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-start-screen-prep-session-response");
		TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		_logger.LogInformation("Start-screen TCG readiness ticket staged. Player={player}, WorldTunnel={worldTunnel}, Ticket={ticket}", playerGuid, worldTunnel, ticket);
		MinigameDiagnosticsLog.Info("TcgPostStartScreenEarlyLaunchSuppressed", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "start-screen-loading-prep"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("suppressedCommands", "TcgDetailUiPatch:SendDatasourceAndPopulate,MinigameDetail:Show,MinigameDetail:Populate,MiniGameStateManager:joinMultiplayerMiniGame,TradingCardGameHandler:Show,TradingCardGameHandler:show,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,accepted-launch fallback,MiniGame:PlayerStartMiniGame,MiniGameCreateGameResultPacket,MiniGame:BeginLoad,HUD:showMinigame"), MinigameDiagnosticsLog.Field("readinessOnly", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgPostStartScreenPrepareCommandsSent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "start-screen-loading-prep"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("sessionId", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("selectedRowId", item), MinigameDiagnosticsLog.Field("selectedTitle", item2), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("prepCommands", value), MinigameDiagnosticsLog.Field("acceptedLaunchScheduled", false), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("readinessGate", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("didShowGo", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
	}

	public static bool SendAcceptedTcgLaunchAfterStartScreenGo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string reason)
	{
		return SendAcceptedTcgLaunch(connection, worldTunnel: false, playerGuid, ticket, reason, allowStartScreenOnlyBypass: true);
	}

	private static bool ShouldSendPostGoLaunchFollowup(ulong playerGuid, bool worldTunnel, string ticket, ulong launchTicket, string reason, string stage, int attempt = 0)
	{
		if (MiniGamePacketHandler.ShouldSendTradingCardPostGoLaunchFollowup(playerGuid, ticket, launchTicket, 7, out var skipReason, out var activeTicket, out var activeLaunchTicket, out var activeMiniGameInstanceId, out var activeState, out var activeGoVisible, out var activeLaunchStarted))
		{
			return true;
		}
		MinigameDiagnosticsLog.Info("TcgPostGoDelayedLaunchSuppressedStaleTicket", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("stage", stage), MinigameDiagnosticsLog.Field("attempt", attempt), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", 7), MinigameDiagnosticsLog.Field("activeTicket", activeTicket ?? string.Empty), MinigameDiagnosticsLog.Field("activeLaunchTicket", activeLaunchTicket), MinigameDiagnosticsLog.Field("activeMiniGameInstanceId", activeMiniGameInstanceId), MinigameDiagnosticsLog.Field("activeState", activeState), MinigameDiagnosticsLog.Field("activeGoVisible", activeGoVisible), MinigameDiagnosticsLog.Field("activeLaunchStarted", activeLaunchStarted), MinigameDiagnosticsLog.Field("skipReason", skipReason), MinigameDiagnosticsLog.Field("suppressedPackets", "ClientActivityLaunch accepted-launch,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,MiniGameCreateGameResultPacket,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("didLaunch", false));
		return false;
	}

	private static (int SelectedRowId, string SelectedTitle, string SelectedDescription) ResolvePostGoSelectedRow(ulong playerGuid, int selectedRowId, out bool hasStoredSelection, out bool storedSelectionFresh, out double selectedRowAgeMs, out DateTime selectedRowStoredUtc)
	{
		hasStoredSelection = TcgMatchmakingState.TryGetTcgDetailPlaySelectionForDiagnostics(playerGuid, out var selectedRowId2, out selectedRowAgeMs, out selectedRowStoredUtc, out storedSelectionFresh);
		int selectedRowId3 = (MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(selectedRowId) ? selectedRowId : 41);
		if ((!MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(selectedRowId) & hasStoredSelection) && MiniGamePacketHandler.IsTradingCardStartScreenDisplayRow(selectedRowId2))
		{
			selectedRowId3 = selectedRowId2;
		}
		var (item, item2, item3) = MiniGamePacketHandler.ResolveTradingCardStartScreenDisplayForDiagnostics(selectedRowId3);
		return (SelectedRowId: item, SelectedTitle: item2, SelectedDescription: item3);
	}

	public static void CompleteTcgLaunch(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket = null, bool sendSessionTicket = true, bool allowStartScreenOnlyBypass = false)
	{
		if (MiniGamePacketHandler.TCG_START_SCREEN_ONLY && !allowStartScreenOnlyBypass)
		{
			if (MiniGamePacketHandler.TryConsumeTradingCardClientActivityLaunchGo(playerGuid, out var ticket2, out var launchTicket, out var miniGameInstanceId, out var selectedDisplayRowId, out var selectedTitle, out var selectedDescription, out var activeState, out var activeGoVisible, out var activeLaunchStarted, out var skipReason))
			{
				string value = ticket ?? string.Empty;
				ticket = ticket2;
				string value2 = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
				MinigameDiagnosticsLog.Info("TcgStartScreenClientActivityLaunchGoRequestObserved", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CompleteTcgLaunch:client-activity-launch-go"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("clientRequestType", "ClientActivityLaunch/CompleteTcgLaunch"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("requestedTicketIgnored", value), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedDisplayRowId), MinigameDiagnosticsLog.Field("selectedTitle", selectedTitle), MinigameDiagnosticsLog.Field("selectedDescription", selectedDescription), MinigameDiagnosticsLog.Field("activeState", activeState), MinigameDiagnosticsLog.Field("activeGoVisible", activeGoVisible), MinigameDiagnosticsLog.Field("activeLaunchStarted", activeLaunchStarted), MinigameDiagnosticsLog.Field("suppressedPacket", "row41 MiniGame:JoinGame/subtype16 bootstrap"), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
				MiniGamePacketHandler.SendTradingCardGameStartedAfterStartScreenGo(connection, worldTunnel, playerGuid, "client-activity-launch-go", miniGameInstanceId, -1, -1);
				CompleteTcgLaunchAfterStartScreenGo(connection, worldTunnel, playerGuid, ticket, selectedDisplayRowId);
				bool flag = SendAcceptedTcgLaunchAfterStartScreenGo(connection, worldTunnel, playerGuid, ticket, "client-activity-launch-go");
				MiniGamePacketHandler.MarkTradingCardClientActivityLaunchGoCommandsSent(playerGuid, ticket, launchTicket, miniGameInstanceId);
				MinigameDiagnosticsLog.Info("TcgStartScreenClientActivityLaunchGoCommandsSent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CompleteTcgLaunch:client-activity-launch-go"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedDisplayRowId), MinigameDiagnosticsLog.Field("launchCommands", "MiniGame:BeginLoad,ClientActivityLaunch:CompleteTcgLaunchAfterStartScreenGo,ClientActivityLaunch:AcceptedLaunch,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("didSendAcceptedLaunchImmediately", flag), MinigameDiagnosticsLog.Field("didSendBeginLoad", true), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
			}
			else
			{
				if (string.IsNullOrEmpty(ticket))
				{
					ticket = TcgSessionRegistry.Register(playerGuid, 7);
				}
				if (sendSessionTicket)
				{
					SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-launch-session-response");
				}
				TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
				MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CompleteTcgLaunch"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("activeSkipReason", skipReason), MinigameDiagnosticsLog.Field("suppressedScripts", "TradingCardGameHandler:Show,TradingCardGameHandler:show,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,MiniGameStateManager:startGame"), MinigameDiagnosticsLog.Field("target", "native MinigameStartScreen loading modal only"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
				MinigameDiagnosticsLog.Info("TcgStartScreenSuppressNativeTcgLaunch", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CompleteTcgLaunch"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("suppressedScripts", "TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
				MinigameDiagnosticsLog.Info("TcgStartScreenSuppressHudShow", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CompleteTcgLaunch"), MinigameDiagnosticsLog.Field("suppressedScripts", "HUD:showMinigame,MiniGameStateManager:startGame"), MinigameDiagnosticsLog.Field("didLaunch", false));
				MiniGamePacketHandler.SendTradingCardStartScreenBootstrap(connection, worldTunnel, playerGuid, 41, 11, "Free Realms Trading Card Game Lobby", "ClientActivityLaunch:CompleteTcgLaunch:start-screen-only");
			}
		}
		else
		{
			if (string.IsNullOrEmpty(ticket))
			{
				ticket = TcgSessionRegistry.Register(playerGuid, 7);
			}
			if (sendSessionTicket)
			{
				SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-launch-session-response");
			}
			bool worldTunnel2 = false;
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
			MinigameDiagnosticsLog.Info("complete-tcg-launch-start", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.Field("ticket", ticket), MinigameDiagnosticsLog.Field("afterStartScreenGo", allowStartScreenOnlyBypass));
			SendTcgInviteSetup(connection, worldTunnel2, playerGuid, ticket, "complete-tcg-launch");
			ScheduleAcceptedLaunchFallback(connection, worldTunnel: false, playerGuid, ticket, "complete-tcg-launch", allowStartScreenOnlyBypass);
			_logger.LogInformation("CompleteTcgLaunch invite staged. Player={player}, WorldTunnel={worldTunnel}, Ticket={ticket}", playerGuid, worldTunnel, ticket);
			MinigameDiagnosticsLog.Info("complete-tcg-launch-awaiting-invite-response", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("ticket", ticket));
		}
	}

	public static void RefreshTcgLaunchTicket(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string reason)
	{
		MinigameDiagnosticsLog.Info("launch-ticket-refresh", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty));
		SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-refresh-session-response");
		if (MiniGamePacketHandler.TCG_START_SCREEN_ONLY)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "RefreshTcgLaunchTicket:" + reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("suppressedScripts", "launch-ticket-refresh invite/gameplay followups"), MinigameDiagnosticsLog.Field("target", "native MinigameStartScreen loading modal only"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressNativeTcgLaunch", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "RefreshTcgLaunchTicket:" + reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("suppressedScripts", "TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressHudShow", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "RefreshTcgLaunchTicket:" + reason), MinigameDiagnosticsLog.Field("suppressedScripts", "HUD:showMinigame,MiniGameStateManager:startGame"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MiniGamePacketHandler.SendTradingCardStartScreenBootstrap(connection, worldTunnel, playerGuid, 41, 11, "Free Realms Trading Card Game Lobby", "ClientActivityLaunch:RefreshTcgLaunchTicket:start-screen-only");
		}
		else
		{
			bool worldTunnel2 = false;
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
			SendTcgInviteSetup(connection, worldTunnel2, playerGuid, ticket, "launch-ticket-refresh");
			ScheduleAcceptedLaunchFallback(connection, worldTunnel: false, playerGuid, ticket, "launch-ticket-refresh");
		}
	}

	private static void SendTcgInviteSetup(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string reason)
	{
		TcgDetailUiPatch.SendDatasourceAndPopulate(connection, worldTunnel, playerGuid, reason + "-activity-list");
		MiniGamePacketHandler.SendTradingCardActivityUnlocks(connection, worldTunnel, playerGuid);
		SendInviteDetails(connection, worldTunnel, playerGuid);
		SendTradingCardUiShow(connection, playerGuid, reason + "-invite-pending");
		MinigameDiagnosticsLog.Info("tcg-invite-setup-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty));
	}

	private static bool SendAcceptedTcgLaunch(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string reason, bool allowStartScreenOnlyBypass = false)
	{
		if (MiniGamePacketHandler.TCG_START_SCREEN_ONLY && !allowStartScreenOnlyBypass)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "SendAcceptedTcgLaunch:" + reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("suppressedScripts", "accepted-launch followups and TradingCardGameHandler portal entry"), MinigameDiagnosticsLog.Field("target", "native MinigameStartScreen loading modal only"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressNativeTcgLaunch", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "SendAcceptedTcgLaunch:" + reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("suppressedScripts", "TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressHudShow", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "SendAcceptedTcgLaunch:" + reason), MinigameDiagnosticsLog.Field("suppressedScripts", "HUD:showMinigame,MiniGameStateManager:startGame"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MiniGamePacketHandler.SendTradingCardStartScreenBootstrap(connection, worldTunnel, playerGuid, 41, 11, "Free Realms Trading Card Game Lobby", "ClientActivityLaunch:AcceptedLaunch:start-screen-only");
			return false;
		}
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		string value = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
		if (allowStartScreenOnlyBypass && !ShouldSendPostGoLaunchFollowup(playerGuid, worldTunnel, ticket, launchTicket, reason, "accepted-launch"))
		{
			return false;
		}
		if (!TcgMatchmakingState.TryMarkLaunchFollowupsSent(playerGuid, ticket))
		{
			MinigameDiagnosticsLog.Info("tcg-accepted-launch-skipped", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty));
			return false;
		}
		SendPlayerAccepted(connection, worldTunnel, playerGuid);
		SendActivityLaunched(connection, worldTunnel, playerGuid, ticket);
		SendMatchmakingStart(connection, worldTunnel, playerGuid);
		SendTradingCardUiShow(connection, playerGuid, reason + "-before-ticket");
		SendTradingCardLaunchTicketSet(connection, worldTunnel, playerGuid, ticket, launchTicket, reason + "-initial", allowStartScreenOnlyBypass);
		if (allowStartScreenOnlyBypass)
		{
			MinigameDiagnosticsLog.Info("TcgPostGoDelayedLaunchRetriesSuppressed", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("suppressedPackets", "delayed MatchmakingStart,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,MiniGameCreateGameResultPacket,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("why", "first GO already emitted the single post-GO launch burst"), MinigameDiagnosticsLog.Field("didLaunch", false));
		}
		else
		{
			ScheduleTradingCardLaunchTicketRetries(connection, worldTunnel, playerGuid, ticket, launchTicket, reason, allowStartScreenOnlyBypass);
		}
		_logger.LogInformation("Accepted TCG launch sent. Player={player}, WorldTunnel={worldTunnel}, Ticket={ticket}", playerGuid, worldTunnel, ticket);
		MinigameDiagnosticsLog.Info("tcg-accepted-launch-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("afterStartScreenGo", allowStartScreenOnlyBypass));
		return true;
	}

	private static void ScheduleAcceptedLaunchFallback(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string reason, bool allowStartScreenOnlyBypass = false)
	{
		Task.Run(async delegate
		{
			await Task.Delay(1500).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				SendAcceptedTcgLaunch(connection, worldTunnel, playerGuid, ticket, reason + "-delayed-followup", allowStartScreenOnlyBypass);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Delayed accepted TCG launch follow-up failed. Player={player}", playerGuid);
				MinigameDiagnosticsLog.Warn("tcg-accepted-launch-delayed-send-failed", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("error", ex.Message));
			}
		});
	}

	private static void SendTradingCardLaunchTicketSet(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, ulong launchTicket, string reason, bool allowStartScreenOnlyBypass = false)
	{
		string value = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
		SendMatchmakingLaunch(connection, worldTunnel, playerGuid, 41, ticket);
		SendTradingCardSelectGameScript(connection, playerGuid, reason);
		if (allowStartScreenOnlyBypass)
		{
			MiniGamePacketHandler.SendTradingCardStartGameAfterStartScreenGo(connection, worldTunnel, playerGuid, launchTicket, reason);
		}
		else
		{
			MiniGamePacketHandler.SendTradingCardStartGame(connection, worldTunnel, playerGuid, launchTicket);
		}
		MiniGamePacketHandler.SendTradingCardGameReady(connection, worldTunnel, playerGuid);
		SendInviteStartGameAck(connection, worldTunnel, playerGuid);
		SendTradingCardUiShow(connection, playerGuid, reason);
		SendTradingCardStartGameScript(connection, playerGuid, reason);
		if (allowStartScreenOnlyBypass)
		{
			MiniGamePacketHandler.SendTradingCardGameStartedAfterStartScreenGo(connection, worldTunnel, playerGuid, reason);
		}
		else
		{
			MiniGamePacketHandler.SendTradingCardGameStarted(connection, worldTunnel, playerGuid);
		}
		MinigameDiagnosticsLog.Info("tcg-launch-ticket-set-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("afterStartScreenGo", allowStartScreenOnlyBypass));
	}

	private static string BuildTcgLaunchCorrelationId(ulong playerGuid, string ticket, ulong launchTicket)
	{
		string value = (string.IsNullOrEmpty(ticket) ? "no-ticket" : ticket);
		return $"{playerGuid}:{value}:{launchTicket}";
	}

	private static void ScheduleTradingCardLaunchTicketRetries(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, ulong launchTicket, string reason, bool allowStartScreenOnlyBypass = false)
	{
		Task.Run(async delegate
		{
			int[] delaysMs = new int[4] { 1500, 2000, 3000, 4000 };
			for (int i = 0; i < delaysMs.Length; i++)
			{
				await Task.Delay(delaysMs[i]).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					if (!allowStartScreenOnlyBypass || ShouldSendPostGoLaunchFollowup(playerGuid, worldTunnel, ticket, launchTicket, reason, "delayed-launch-ticket-retry", i + 1))
					{
						SendMatchmakingStart(connection, worldTunnel, playerGuid);
						SendTradingCardLaunchTicketSet(connection, worldTunnel, playerGuid, ticket, launchTicket, $"{reason}-delayed-ticket-{i + 1}", allowStartScreenOnlyBypass);
					}
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Delayed TCG launch ticket send failed. Player={player}, Attempt={attempt}", playerGuid, i + 1);
					MinigameDiagnosticsLog.Warn("tcg-launch-ticket-delayed-send-failed", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("attempt", i + 1), MinigameDiagnosticsLog.Field("error", ex.Message));
				}
			}
		});
	}

	private static void SendLaunchSessionTicket(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket, string eventName)
	{
		OneTimeSessionPacketHandler.SendSessionResponse(connection, worldTunnel, playerGuid, 41, ticket, eventName);
		if (worldTunnel)
		{
			OneTimeSessionPacketHandler.SendSessionResponse(connection, worldTunnel: false, playerGuid, 41, ticket, eventName + "-client-tunnel-mirror");
		}
	}

	public static void SendActivityLaunched(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string ticket)
	{
		using PacketWriter packetWriter = new PacketWriter();
		WriteActivityLaunchBase(packetWriter, 5, GetLaunchRequestId(playerGuid), 526123817);
		packetWriter.Write(1);
		packetWriter.Write(playerGuid);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogDebug("ActivityLaunched. Player={player}, Activity={activity}", playerGuid, 7);
		MinigameDiagnosticsLog.Info("activity-launched-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("launchRequestId", GetLaunchRequestId(playerGuid)), MinigameDiagnosticsLog.Field("processorHash", 526123817), MinigameDiagnosticsLog.Field("partOfLaunch", true), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendPlayerAccepted(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		int num = 387;
		using PacketWriter packetWriter = new PacketWriter();
		WriteActivityLaunchBase(packetWriter, 4, num, 526123817);
		packetWriter.Write(387);
		packetWriter.Write(41);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("player-accepted-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchRequestId", num), MinigameDiagnosticsLog.Field("processorHash", 526123817), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendInviteDetails(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		int launchRequestId = GetLaunchRequestId(playerGuid);
		using PacketWriter packetWriter = new PacketWriter();
		WriteActivityLaunchBase(packetWriter, 1, launchRequestId, 526123817);
		packetWriter.Write((byte)1);
		packetWriter.Write(playerGuid);
		packetWriter.Write(0);
		packetWriter.Write(launchRequestId);
		packetWriter.Write(1);
		packetWriter.Write(1);
		packetWriter.Write(playerGuid);
		packetWriter.Write(GetLaunchMemberName(connection, playerGuid));
		packetWriter.Write((byte)74);
		packetWriter.Write((byte)1);
		WriteInviteLaunchRequestTail(packetWriter, playerGuid, launchRequestId);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("invite-details-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchRequestId", launchRequestId), MinigameDiagnosticsLog.Field("processorHash", 526123817), MinigameDiagnosticsLog.Field("memberState", "0x4A"), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void WriteInviteLaunchRequestTail(PacketWriter packetWriter, ulong playerGuid, int launchRequestId)
	{
		packetWriter.Write(playerGuid);
		packetWriter.Write(526123817);
		packetWriter.Write(41);
		packetWriter.Write((byte)1);
		packetWriter.Write(launchRequestId);
		packetWriter.Write(526123817);
		packetWriter.Write(11);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)0);
		packetWriter.Write((byte)0);
		packetWriter.Write(7);
		packetWriter.Write(41);
		packetWriter.Write(0);
		packetWriter.Write(0);
	}

	private static void SendInviteStartGameAck(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)180);
		packetWriter.Write(3);
		packetWriter.Write(playerGuid);
		packetWriter.Write(7);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("invite-start-game-ack-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	public static void SendMatchmakingStart(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		int launchRequestId = GetLaunchRequestId(playerGuid);
		WriteActivityLaunchBase(packetWriter, 14, launchRequestId, 526123817);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		SendActivityLaunchEvent(connection, playerGuid, "matchmaking-start-event-bridge", "ActivityEvents:OnMatchmakingStart", launchRequestId, worldTunnel);
		MinigameDiagnosticsLog.Info("matchmaking-start-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.Field("launchRequestId", GetLaunchRequestId(playerGuid)), MinigameDiagnosticsLog.Field("processorHash", 526123817), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void WriteActivityLaunchBase(PacketWriter packetWriter, int packetType, int argA, int argB)
	{
		packetWriter.Write((short)175);
		packetWriter.Write(packetType);
		packetWriter.Write(argA);
		packetWriter.Write(argB);
	}

	private static int GetLaunchRequestId(ulong playerGuid)
	{
		return 387;
	}

	private static string GetLaunchMemberName(GatewayConnection connection, ulong playerGuid)
	{
		if (connection.Player == null)
		{
			return $"Player {playerGuid}";
		}
		string obj = connection.Player.Name.FirstName ?? string.Empty;
		string text = connection.Player.Name.LastName ?? string.Empty;
		string text2 = (obj + " " + text).Trim();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return $"Player {playerGuid}";
	}

	private static string QuoteNativeTcgArgument(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value ?? string.Empty;
		}
		if (!NeedsNativeTcgQuoting(value))
		{
			return value;
		}
		string text = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		return "\"" + text + "\"";
	}

	private static bool NeedsNativeTcgQuoting(string value)
	{
		foreach (char c in value)
		{
			if (char.IsWhiteSpace(c) || c == '"')
			{
				return true;
			}
		}
		return false;
	}

	private static string BuildNativeTcgStartupName(string value, out string reason)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length <= 16)
		{
			reason = "display-name-within-native-limit";
			return text;
		}
		string text2 = CompactNativeTcgName(text);
		if (!string.IsNullOrWhiteSpace(text2) && text2.Length <= 16)
		{
			reason = "whitespace-compacted-to-native-limit";
			return text2;
		}
		string text3 = FirstNativeTcgNameToken(text);
		if (!string.IsNullOrWhiteSpace(text3) && text3.Length <= 16)
		{
			reason = "first-token-fallback";
			return text3;
		}
		reason = "hard-truncated-to-native-limit";
		return text.Substring(0, Math.Min(text.Length, 16));
	}

	private static string CompactNativeTcgName(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (!char.IsWhiteSpace(c) && c != '"' && c != '\\')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string FirstNativeTcgNameToken(string value)
	{
		string[] array = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return value;
	}

	private static bool SendMatchmakingStartPacket(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		SendMatchmakingStart(connection, worldTunnel, playerGuid);
		return true;
	}

	private static void SendTradingCardUiShow(GatewayConnection connection, ulong playerGuid, string reason)
	{
		(string, int[])[] tradingCardNativeEntryScripts = TradingCardNativeEntryScripts;
		for (int i = 0; i < tradingCardNativeEntryScripts.Length; i++)
		{
			var (script, args) = tradingCardNativeEntryScripts[i];
			SendTradingCardUiScript(connection, playerGuid, reason, script, worldTunnel: false, args);
			SendTradingCardUiScript(connection, playerGuid, reason, script, worldTunnel: true, args);
		}
		string[] tradingCardUiLaunchScripts = TradingCardUiLaunchScripts;
		foreach (string script2 in tradingCardUiLaunchScripts)
		{
			SendTradingCardUiScript(connection, playerGuid, reason, script2, worldTunnel: false);
			SendTradingCardUiScript(connection, playerGuid, reason, script2, worldTunnel: true);
		}
	}

	private static void SendTradingCardPostGoLobbyScripts(GatewayConnection connection, ulong playerGuid, string reason)
	{
		string[] tradingCardPostGoLobbyScripts = TradingCardPostGoLobbyScripts;
		foreach (string script in tradingCardPostGoLobbyScripts)
		{
			SendTradingCardUiScript(connection, playerGuid, reason, script, worldTunnel: false);
			SendTradingCardUiScript(connection, playerGuid, reason, script, worldTunnel: true);
		}
	}

	private static void SendTradingCardStartGameScript(GatewayConnection connection, ulong playerGuid, string reason)
	{
		int[] args = new int[1] { 387 };
		SendTradingCardUiScript(connection, playerGuid, reason + "-direct-start", "MiniGameStateManager:setSelectedGameId", worldTunnel: false, args);
		SendTradingCardUiScript(connection, playerGuid, reason + "-direct-start", "MiniGameStateManager:setSelectedGameId", worldTunnel: true, args);
		SendTradingCardUiScript(connection, playerGuid, reason + "-direct-start", "MiniGameStateManager:startGame", worldTunnel: false, args);
		SendTradingCardUiScript(connection, playerGuid, reason + "-direct-start", "MiniGameStateManager:startGame", worldTunnel: true, args);
	}

	private static void SendTradingCardSelectGameScript(GatewayConnection connection, ulong playerGuid, string reason)
	{
		int[] args = new int[1] { 387 };
		SendTradingCardUiScript(connection, playerGuid, reason + "-select-game", "MiniGameStateManager:setSelectedGameId", worldTunnel: false, args);
		SendTradingCardUiScript(connection, playerGuid, reason + "-select-game", "MiniGameStateManager:setSelectedGameId", worldTunnel: true, args);
	}

	private static void SendTradingCardUiScript(GatewayConnection connection, ulong playerGuid, string reason, string script, bool worldTunnel, int[] args = null)
	{
		int[] array = args ?? Array.Empty<int>();
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(script);
		packetWriter.Write(array);
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("tcg-ui-script-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", script), MinigameDiagnosticsLog.Field("paramCount", array.Length), MinigameDiagnosticsLog.Field("params", string.Join(",", array)), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendActivityLaunchEvent(GatewayConnection connection, ulong playerGuid, string reason, string script, int launchRequestId, bool worldTunnel)
	{
		SendTradingCardUiScript(connection, playerGuid, reason, script, worldTunnel, new int[1] { launchRequestId });
	}

	private static bool HandleOwnerMatchmakingRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		int result = 41;
		reader.TryRead(out result);
		TcgMatchmakingState.JoinQueue(playerGuid, result);
		MinigameDiagnosticsLog.Info("owner-matchmaking-request", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result));
		CompleteTcgLaunch(connection, worldTunnel, playerGuid);
		return true;
	}

	private static bool HandleOwnerLaunchRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		MinigameDiagnosticsLog.Info("owner-launch-request", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		CompleteTcgLaunch(connection, worldTunnel, playerGuid);
		return true;
	}

	private static bool HandleInviteMemberRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)175);
		packetWriter.Write(3);
		packetWriter.Write(playerGuid);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("invite-member-response-sent", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool HandleInviteResponse(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		ReadOnlySpan<byte> remainingSpan = reader.RemainingSpan;
		int result = 0;
		int result2 = 0;
		byte result3 = 1;
		int result4 = 0;
		bool flag = reader.TryRead(out result);
		bool flag2 = reader.TryRead(out result2);
		bool num = reader.TryRead(out result3);
		bool flag3 = reader.TryRead(out result4);
		bool flag4 = !num || result3 != 0;
		MinigameDiagnosticsLog.Info("invite-response", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchRequestId", flag ? result : 0), MinigameDiagnosticsLog.Field("processorHash", flag2 ? result2 : 0), MinigameDiagnosticsLog.Field("accepted", flag4), MinigameDiagnosticsLog.Field("declineReason", flag3 ? result4 : 0), MinigameDiagnosticsLog.HexField("remaining", remainingSpan));
		if (!flag4)
		{
			TcgMatchmakingState.MarkLaunchDeclined(playerGuid);
			MinigameDiagnosticsLog.Warn("invite-response-declined", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("launchRequestId", flag ? result : 0), MinigameDiagnosticsLog.Field("declineReason", flag3 ? result4 : 0));
			return true;
		}
		if (!TcgMatchmakingState.TryGetRecentLaunchTicket(playerGuid, out var ticket))
		{
			ticket = TcgSessionRegistry.Register(playerGuid, 7);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
			SendLaunchSessionTicket(connection, worldTunnel, playerGuid, ticket, "tcg-invite-accepted-session-response");
			MinigameDiagnosticsLog.Warn("invite-response-missing-pending-ticket", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("ticket", ticket));
		}
		if (MiniGamePacketHandler.TCG_START_SCREEN_ONLY)
		{
			ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
			if (MiniGamePacketHandler.ShouldSendTradingCardPostGoLaunchFollowup(playerGuid, ticket, launchTicket, 7, out var skipReason, out var activeTicket, out var activeLaunchTicket, out var activeMiniGameInstanceId, out var activeState, out var activeGoVisible, out var activeLaunchStarted))
			{
				SendAcceptedTcgLaunchAfterStartScreenGo(connection, worldTunnel: false, playerGuid, ticket, "invite-response-accepted-post-go");
				return true;
			}
			MinigameDiagnosticsLog.Info("TcgInviteResponseStartScreenOnlySuppressed", "ClientActivityLaunchPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "invite-response-accepted"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("activeTicket", activeTicket ?? string.Empty), MinigameDiagnosticsLog.Field("activeLaunchTicket", activeLaunchTicket), MinigameDiagnosticsLog.Field("activeMiniGameInstanceId", activeMiniGameInstanceId), MinigameDiagnosticsLog.Field("activeState", activeState), MinigameDiagnosticsLog.Field("activeGoVisible", activeGoVisible), MinigameDiagnosticsLog.Field("activeLaunchStarted", activeLaunchStarted), MinigameDiagnosticsLog.Field("skipReason", skipReason), MinigameDiagnosticsLog.Field("suppressedPacket", "ClientActivityLaunch:AcceptedLaunch:start-screen-only row41 bootstrap"), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
			return true;
		}
		SendAcceptedTcgLaunch(connection, worldTunnel: false, playerGuid, ticket, "invite-response-accepted");
		return true;
	}

	private static bool LogUnhandled(GatewayConnection connection, int packetType, bool worldTunnel)
	{
		_logger.LogInformation("Unhandled activity launch packet type={type}, Player={player}, WorldTunnel={worldTunnel}", packetType, connection.Player?.Guid ?? 0, worldTunnel);
		MinigameDiagnosticsLog.Warn("unhandled-packet", "ClientActivityLaunchPacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "ClientActivityLaunch"), MinigameDiagnosticsLog.Field("type", packetType));
		return true;
	}
}
