using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MiniGamePacketHandler
{
	private enum TcgStartScreenReadinessState
	{
		None,
		StartScreenOpened,
		PreparingTcgAssets,
		WaitingForClientAssetsReady,
		AssetsReady,
		GoVisible,
		GoClicked,
		LaunchInProgress,
		LaunchCommandsSent,
		AwaitingTcgDllLoad,
		AwaitingTcgServerConnect,
		TcgConnected,
		LaunchComplete
	}

	private sealed class TcgStartScreenFlowState
	{
		public DateTime OpenedUtc = DateTime.MinValue;

		public DateTime PreparingStartedUtc = DateTime.MinValue;

		public DateTime WaitingForClientAssetsReadyUtc = DateTime.MinValue;

		public DateTime AssetsReadyUtc = DateTime.MinValue;

		public DateTime ReadyUtc = DateTime.MinValue;

		public DateTime GoVisibleUtc = DateTime.MinValue;

		public DateTime GoClickedUtc = DateTime.MinValue;

		public DateTime LaunchInProgressUtc = DateTime.MinValue;

		public DateTime LaunchCommandsSentUtc = DateTime.MinValue;

		public DateTime LaunchCompleteUtc = DateTime.MinValue;

		public bool ReadySent;

		public bool AssetsReadyResponseSent;

		public bool GoConfirmed;

		public bool LaunchStarted;

		public int PreReadyRequestStartGameCount;

		public int SelectedRowId = 41;

		public int MiniGameInstanceId = 41;

		public string Ticket = string.Empty;

		public ulong LaunchTicket;

		public TcgStartScreenReadinessState State;
	}

	private sealed class TcgStartScreenGoLaunchState
	{
		public DateTime OpenedUtc = DateTime.MinValue;

		public DateTime PreparingStartedUtc = DateTime.MinValue;

		public DateTime FirstGoUtc = DateTime.MinValue;

		public DateTime LaunchStartedUtc = DateTime.MinValue;

		public DateTime LaunchCommandsSentUtc = DateTime.MinValue;

		public DateTime LaunchCompleteUtc = DateTime.MinValue;

		public DateTime CreateMiniGameResponseUtc = DateTime.MinValue;

		public DateTime AssetsReadyResponseUtc = DateTime.MinValue;

		public int MiniGameInstanceId;

		public string Ticket = string.Empty;

		public ulong LaunchTicket;

		public readonly object Sync = new object();
	}

	private readonly struct TradingCardGroupElementRow
	{
		public readonly int LinkId;

		public readonly int TargetMiniGameDataId;

		public readonly int NameId;

		public readonly int DescriptionId;

		public readonly int IconId;

		public readonly int Position;

		public readonly int Difficulty;

		public readonly string DetailImage;

		public readonly string ThumbnailImage;

		public readonly string DebugName;

		public TradingCardGroupElementRow(int linkId, int targetMiniGameDataId, int nameId, int descriptionId, int iconId, int position, int difficulty, string detailImage, string thumbnailImage, string debugName)
		{
			LinkId = linkId;
			TargetMiniGameDataId = targetMiniGameDataId;
			NameId = nameId;
			DescriptionId = descriptionId;
			IconId = iconId;
			Position = position;
			Difficulty = difficulty;
			DetailImage = detailImage;
			ThumbnailImage = thumbnailImage;
			DebugName = debugName;
		}
	}

	private readonly struct TradingCardStartScreenDisplayMetadata
	{
		public readonly int RequestedRowId;

		public readonly int DisplayRowId;

		public readonly int NameId;

		public readonly int MiniGameInfoNameId;

		public readonly int IconId;

		public readonly int DescriptionId;

		public readonly int MiniGameInfoDescriptionId;

		public readonly int Difficulty;

		public readonly string Title;

		public readonly string Description;

		public readonly string DetailImage;

		public readonly bool RequestedKnownRow;

		public TradingCardStartScreenDisplayMetadata(int requestedRowId, int displayRowId, int nameId, int miniGameInfoNameId, int iconId, int descriptionId, int miniGameInfoDescriptionId, int difficulty, string title, string description, string detailImage, bool requestedKnownRow)
		{
			RequestedRowId = requestedRowId;
			DisplayRowId = displayRowId;
			NameId = nameId;
			MiniGameInfoNameId = miniGameInfoNameId;
			IconId = iconId;
			DescriptionId = descriptionId;
			MiniGameInfoDescriptionId = miniGameInfoDescriptionId;
			Difficulty = difficulty;
			Title = title;
			Description = description;
			DetailImage = detailImage;
			RequestedKnownRow = requestedKnownRow;
		}
	}

	public static readonly bool TCG_START_SCREEN_ONLY = true;

	private const string TCG_START_SCREEN_HEADER_EXPERIMENT = "row41";

	private const string TCG_START_SCREEN_FLAG_EXPERIMENT = "baseline_all_false";

	private const bool TCG_START_SCREEN_SHOW_GATE_0X267 = true;

	private static readonly int[] TradingCardActivityGameIds = new int[4] { 41, 603, 604, 602 };

	private static readonly int[] TradingCardPoeCandidateGameIds = new int[2] { 499, 507 };

	private static readonly int[] TradingCardCarouselAliasIds = Array.Empty<int>();

	private static readonly int[] TradingCardLegacyCarouselAliasIds = Array.Empty<int>();

	private const int TradingCardDetailGroupId = 36;

	private const int TradingCardPreselectedGameId = 41;

	private const int TradingCardNativeDetailMiniGameId = 727;

	private const int TradingCardCategoryId = 11;

	private const int TradingCardGroupNameId = 3388;

	private const int TradingCardGroupDescriptionId = 401571;

	private const int TradingCardGroupIconId = 7533;

	private const int TradingCardGroupSettingsIconId = 0;

	private const int TradingCardLobbyRowId = 41;

	private const int PacketReadyTradingCardLobbyActivityId = 7;

	private const int PacketReadyTradingCardLobbyNameId = 92784;

	private const int PacketReadyTradingCardLobbyDescriptionId = 92785;

	private const int PacketReadyTradingCardImageSetId = 7591;

	private const int TradingCardLobbyNameId = 92784;

	private const int TradingCardLobbyDescriptionId = 92785;

	private const int TradingCardLobbyIconId = 7591;

	private const int BryTournamentOriginalNameId = -1171390943;

	private const int BryTournamentOriginalDescriptionId = -903091888;

	private const int BryTrickOriginalNameId = 647264565;

	private const int BryTrickOriginalDescriptionId = 90954707;

	private const int TcgTutorialOriginalNameId = -711660947;

	private const int TcgTutorialOriginalDescriptionId = -1608897101;

	private const int TradingCardPracticeIconId = 9870;

	private const string TradingCardLobbyTitle = "Free Realms Trading Card Game Lobby";

	private const string TradingCardLobbyDescription = "Enter the TCG and you can learn to play, practice against Kai in a single-player match, or take on other Card Duelists! You can also check out your collection, trade with other players, or build your own decks!";

	private const string BryTournamentTitle = "Bry's Tournament Deck";

	private const string BryTournamentDescription = "As a Card Duelist; Beat Bry's Tournament Deck";

	private const string BryTrickTitle = "Bry's Trick Deck";

	private const string BryTrickDescription = "Play and beat Bry's Trick Deck. Bry can be found in Lakeshore.";

	private const string TcgTutorialTitle = "Free Realms Trading Card Game Tutorial";

	private const string TcgTutorialDescription = "Learn how to play the Free Realms Trading Card Game.";

	private const string TradingCardGroupInfoWireOrder = "baseMiniGameHeader0,baseMiniGameHeader1,baseMiniGameHeader2,groupId,groupNameToken,groupDescriptionToken,groupIconId,backgroundSwf,preselectedGameId,elementCount,elements,stageProgression,showStartScreenOnPlayNext,settingsIconId";

	private const string TradingCardGroupElementWireOrder = "linkId,targetMiniGameDataId,detailImage,thumbnailImage,unlockedFlag,quantity,nameToken,descriptionToken,iconId,parentMiniGameDataId,membersOnlyFlag,mysteryChestIcon,levelString,difficulty,position,stageNumber,mustPurchaseItemId,priceItemId,enabledFlag";

	private const string TradingCardMiniGameInfoWireOrder = "baseMiniGamePacket(family=39,subtype,header1,header2,header3),miniGameInfoData(nameToken,iconId,descriptionToken,difficulty,profileType,type,membersOnly,resources,objectiveCount,flags,unknownString,unknown14,unknown15,preselectedGameId/displayRowId,unknown16,unknown17,unknown18,unknown19,leaderboardId/displayRowId)";

	private const string BaseClientMiniGameStartScreenColumns = "0=Name,1=IconID,2=Description,3=Difficulty,4=Type,7=MembersOnly,8=ProfileType,11=MiniGameId/SelectedDisplayRowId,16=LeaderboardId/SelectedDisplayRowId";

	private static readonly TradingCardGroupElementRow[] TradingCardDefaultGroupElements = new TradingCardGroupElementRow[4]
	{
		new TradingCardGroupElementRow(41, 41, 3388, 401571, 9867, 0, 1, "tcg_lobby_detail.dds", "tcg_lobby_thumb.dds", "Free Realms Trading Card Game Lobby"),
		new TradingCardGroupElementRow(603, 603, 451603, 451612, 9870, 1, 1, "brys_tournament_detail.dds", "brys_tournament_thumb.dds", "Bry's Tournament Deck"),
		new TradingCardGroupElementRow(604, 604, 451604, 451613, 9870, 2, 1, "brys_trick_detail.dds", "brys_trick_thumb.dds", "Bry's Trick Deck"),
		new TradingCardGroupElementRow(602, 602, 451602, 451614, 9870, 3, 1, "tcg_tutorial_detail.dds", "tcg_tutorial_thumb.dds", "Free Realms Trading Card Game Tutorial")
	};

	private static readonly TradingCardGroupElementRow PoeTournamentGroupElement = new TradingCardGroupElementRow(499, 507, 451499, 451509, 9870, 4, 1, "poes_tournament_detail.dds", "poes_tournament_thumb.dds", "Poe's Tournament Deck");

	private static readonly object TcgStartScreenFlowSync = new object();

	private static readonly ConcurrentDictionary<ulong, TcgStartScreenFlowState> TcgStartScreenFlows = new ConcurrentDictionary<ulong, TcgStartScreenFlowState>();

	private static readonly ConcurrentDictionary<string, TcgStartScreenGoLaunchState> TcgStartScreenGoLaunchGuards = new ConcurrentDictionary<string, TcgStartScreenGoLaunchState>();

	private static readonly TimeSpan TcgStartScreenGoLaunchGuardTtl = TimeSpan.FromMinutes(10.0);

	private const int TcgAssetsReadyTimeoutFallbackDelayMs = 2500;

	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MiniGamePacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, ReadOnlySpan<byte> rawPayload, bool worldTunnel)
	{
		if (!reader.TryRead(out byte result))
		{
			_logger.LogError("Failed to read minigame packet type.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "MiniGamePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "MiniGame"), MinigameDiagnosticsLog.Field("reason", "missing packet type"), MinigameDiagnosticsLog.HexField("payload", rawPayload));
			return false;
		}
		ulong num = connection.Player?.Guid ?? 0;
		_logger.LogInformation("MiniGameRequest. Type={type}, Player={player}, WorldTunnel={worldTunnel}, Remaining={remaining}", result, num, worldTunnel, Convert.ToHexString(reader.RemainingSpan));
		MinigameDiagnosticsLog.Info("packet-received", "MiniGamePacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("family", "MiniGame"), MinigameDiagnosticsLog.Field("type", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 39, "MiniGamePacketHandler", "MiniGame", result, reader.RemainingSpan);
		return result switch
		{
			4 => HandleCreateMiniGameRequest(connection, reader, worldTunnel, num), 
			5 => HandleTradingCardClientStartRequest(connection, reader, worldTunnel, num, "client-start-request", uiOnlyResponseForTcgLobby: true), 
			14 => EchoSubpacket(connection, rawPayload, worldTunnel), 
			38 => HandleRequestTcgChallenge(connection, reader, worldTunnel, num), 
			40 => HandleTradingCardStartGame(connection, reader, worldTunnel, num), 
			48 => HandleMiniGameGroupRequest(connection, reader, worldTunnel, num, "CreateMiniGameGroup"), 
			49 => HandleMiniGameGroupRequest(connection, reader, worldTunnel, num, "RequestMiniGameGroup"), 
			37 => HandleTradingCardClientStartRequest(connection, reader, worldTunnel, num, "client-assets-ready-request", uiOnlyResponseForTcgLobby: false), 
			67 => HandleCreateGameResult(connection, reader, worldTunnel, num), 
			6 => HandleMiniGameEndOrCancelRequest(connection, reader, worldTunnel, num, result, "MiniGameEndPacket"), 
			7 => HandleMiniGameEndOrCancelRequest(connection, reader, worldTunnel, num, result, "MiniGameCancelPacket"), 
			1 => AckSubpacket(connection, 2, worldTunnel), 
			2 => AckSubpacket(connection, 1, worldTunnel), 
			16 => AckSubpacket(connection, 16, worldTunnel), 
			18 => AckSubpacket(connection, 18, worldTunnel), 
			_ => LogUnhandled(result, num, worldTunnel, reader.RemainingSpan), 
		};
	}

	private static bool HandleMiniGameEndOrCancelRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid, byte requestType, string requestName)
	{
		int result = 0;
		int result2 = -1;
		int result3 = -1;
		reader.TryRead(out result);
		reader.TryRead(out result2);
		reader.TryRead(out result3);
		ReadOnlySpan<byte> remainingSpan = reader.RemainingSpan;
		MinigameDiagnosticsLog.Info("MiniGameControlPacketObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("requestType", requestType), MinigameDiagnosticsLog.Field("requestName", requestName), MinigameDiagnosticsLog.Field("stateId", result), MinigameDiagnosticsLog.Field("groupId", result2), MinigameDiagnosticsLog.Field("gameId", result3), MinigameDiagnosticsLog.HexField("remaining", remainingSpan));
		return SendMiniGameLeave(connection, worldTunnel, playerGuid, result, requestType, requestName);
	}

	private static bool HandleMiniGameGroupRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid, string requestName)
	{
		int result = 0;
		int result2 = 0;
		int result3 = 0;
		reader.TryRead(out result);
		reader.TryRead(out result2);
		reader.TryRead(out result3);
		bool flag = IsTradingCardGroupRequest(result) || IsTradingCardGroupRequest(result2) || IsTradingCardGroupRequest(result3);
		MinigameDiagnosticsLog.Info("minigame-group-request-observed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("request", requestName), MinigameDiagnosticsLog.Field("fieldA", result), MinigameDiagnosticsLog.Field("fieldB", result2), MinigameDiagnosticsLog.Field("fieldC", result3), MinigameDiagnosticsLog.Field("parsedGroupId", result3), MinigameDiagnosticsLog.Field("parsedGameId", result2), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("willSendGroupInfo", flag), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("rootCauseCandidate", "The decompiled client consumes subtype 50 MiniGameGroupInfo before it exposes BaseClient.MiniGameGroup rows to MinigameDetail. Send complete group 36 rows here when the request references TCG."));
		if (flag)
		{
			SendTradingCardMiniGameGroupInfo(connection, worldTunnel, playerGuid, requestName + "-response");
		}
		return true;
	}

	private static bool HandleCreateMiniGameRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		int result = 0;
		int result2 = 0;
		int result3 = 0;
		reader.TryRead(out result);
		reader.TryRead(out result2);
		reader.TryRead(out result3);
		if (result == 0)
		{
			result = 387;
		}
		if (!TcgMatchmakingState.TryGetRecentLaunchTicket(playerGuid, out var ticket))
		{
			int item = GetTradingCardStartScreenHeader().StateId;
			ticket = TcgSessionRegistry.Register(playerGuid, item);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		}
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		MinigameDiagnosticsLog.Info("create-minigame-request", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("requestedGameId", result), MinigameDiagnosticsLog.Field("requestArgA", result2), MinigameDiagnosticsLog.Field("requestArgB", result3), MinigameDiagnosticsLog.Field("ticket", ticket), MinigameDiagnosticsLog.Field("launchTicket", launchTicket));
		bool flag = result == 387 || Array.IndexOf(TradingCardActivityGameIds, result) >= 0;
		if (flag || IsTradingCardCarouselAliasId(result))
		{
			LogPlayClickedFromMiniGameCreate(playerGuid, worldTunnel, result, result2, result3, ticket, launchTicket, flag);
		}
		if (flag)
		{
			if (TCG_START_SCREEN_ONLY && TryHandleTradingCardPostGoCreateMiniGameRequest(connection, worldTunnel, playerGuid, result, result2, result3, ticket, launchTicket))
			{
				return true;
			}
			bool usedRecentDetailSelection;
			double detailSelectionAgeMs;
			TradingCardStartScreenDisplayMetadata displayMetadata = ResolveTradingCardStartScreenDisplayMetadata(ResolveCreateMiniGameStartScreenDisplayRow(playerGuid, result, out usedRecentDetailSelection, out detailSelectionAgeMs));
			LogCreateMiniGameTransition(playerGuid, worldTunnel, result, result2, result3, ticket, launchTicket, displayMetadata, usedRecentDetailSelection, detailSelectionAgeMs);
			SendTradingCardStartScreenBootstrap(connection, worldTunnel, playerGuid, displayMetadata.DisplayRowId, 11, displayMetadata.Title, "CreateMiniGameRequest");
			if (TCG_START_SCREEN_ONLY)
			{
				MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "CreateMiniGameRequest"), MinigameDiagnosticsLog.Field("requestedGameId", result), MinigameDiagnosticsLog.Field("suppressedPackets", "RequestStartGame/subtype5 response,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("allowedPackets", "controlled MiniGame:JoinGame/subtype16 start-screen opener with MiniGameInfo+0x267 show gate=true"), MinigameDiagnosticsLog.Field("didLaunch", false));
				return true;
			}
			SendTradingCardJoinGame(connection, worldTunnel, playerGuid);
			SendTradingCardStartGame(connection, worldTunnel, playerGuid, launchTicket);
			SendTradingCardGameStarted(connection, worldTunnel, playerGuid);
			SendTradingCardGameReady(connection, worldTunnel, playerGuid);
			return true;
		}
		return LogUnhandled(4, playerGuid, worldTunnel, reader.RemainingSpan);
	}

	private static void LogPlayClickedFromMiniGameCreate(ulong playerGuid, bool worldTunnel, int requestedGameId, int requestArgA, int requestArgB, string ticket, ulong launchTicket, bool willLaunch)
	{
		MinigameDiagnosticsLog.Info("PlayClicked", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", requestedGameId), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "BaseMiniGameCreateMiniGameRequest"), MinigameDiagnosticsLog.Field("didLaunch", willLaunch), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("playGuid", requestedGameId), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("isNativeTcgMinigame", requestedGameId == 387 || requestedGameId == 727), MinigameDiagnosticsLog.Field("isCarouselRowId", Array.IndexOf(TradingCardActivityGameIds, requestedGameId) >= 0), MinigameDiagnosticsLog.Field("isCarouselAliasId", IsTradingCardCarouselAliasId(requestedGameId)), MinigameDiagnosticsLog.Field("preselectedDetailRowId", 41), MinigameDiagnosticsLog.Field("expectedNativeTcgMinigameId", 387), MinigameDiagnosticsLog.Field("expectedNativeTcgDetailMiniGameId", 727), MinigameDiagnosticsLog.Field("ticket", ticket), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("note", "Server-visible request after MinigameDetail PLAY; Flash FR_event may be translated before it reaches the gateway."));
	}

	private static int ResolveCreateMiniGameStartScreenDisplayRow(ulong playerGuid, int requestedGameId, out bool usedRecentDetailSelection, out double detailSelectionAgeMs)
	{
		usedRecentDetailSelection = false;
		detailSelectionAgeMs = -1.0;
		if (IsTradingCardStartScreenDisplayRow(requestedGameId))
		{
			return requestedGameId;
		}
		if ((requestedGameId == 387 || requestedGameId == 727) && TcgMatchmakingState.TryGetRecentTcgDetailPlaySelection(playerGuid, out var selectedRowId, out detailSelectionAgeMs))
		{
			usedRecentDetailSelection = true;
			return selectedRowId;
		}
		return 41;
	}

	private static void LogCreateMiniGameTransition(ulong playerGuid, bool worldTunnel, int requestedGameId, int requestArgA, int requestArgB, string ticket, ulong launchTicket, TradingCardStartScreenDisplayMetadata displayMetadata, bool usedRecentDetailSelection, double detailSelectionAgeMs)
	{
		var (num, num2, num3) = GetTradingCardStartScreenHeader();
		MinigameDiagnosticsLog.Info("CreateMiniGameObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("selectedTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("selectedDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("currentCategoryId", 11), MinigameDiagnosticsLog.Field("currentRowTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("usedRecentDetailSelection", usedRecentDetailSelection), MinigameDiagnosticsLog.Field("detailSelectionAgeMs", detailSelectionAgeMs), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenGroupId", num2), MinigameDiagnosticsLog.Field("startScreenGameId", num3), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("ticket", ticket), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("clientApi", "Ui.CreateMiniGame"), MinigameDiagnosticsLog.Field("expectedNativeCommand", "MiniGame:JoinGame -> MiniGame:PlayerStartMiniGame -> MiniGameCreateGameResultPacket"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
	}

	private static bool HandleTradingCardClientStartRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid, string reason, bool uiOnlyResponseForTcgLobby)
	{
		int result = 0;
		int result2 = 0;
		int result3 = 0;
		reader.TryRead(out result);
		reader.TryRead(out result2);
		reader.TryRead(out result3);
		string text = string.Empty;
		ulong num = 0uL;
		if (TcgMatchmakingState.TryGetRecentLaunchTicket(playerGuid, out var ticket))
		{
			text = ticket;
			num = TcgSessionRegistry.GetLaunchTicket(playerGuid, text);
		}
		_logger.LogInformation("TradingCardClientStartRequest. Reason={reason}, Player={player}, Instance={instance}, ArgA={argA}, ArgB={argB}, LaunchTicket={launchTicket}", reason, playerGuid, result, result2, result3, num);
		MinigameDiagnosticsLog.Info("tcg-client-start-request", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("miniGameInstanceId", result), MinigameDiagnosticsLog.Field("requestArgA", result2), MinigameDiagnosticsLog.Field("requestArgB", result3), MinigameDiagnosticsLog.Field("ticket", text), MinigameDiagnosticsLog.Field("launchTicket", num), MinigameDiagnosticsLog.Field("uiOnlyResponseCandidate", uiOnlyResponseForTcgLobby && result == 41));
		if (string.IsNullOrEmpty(text))
		{
			int item = GetTradingCardStartScreenHeader().StateId;
			text = TcgSessionRegistry.Register(playerGuid, item);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, text);
			num = TcgSessionRegistry.GetLaunchTicket(playerGuid, text);
		}
		if (TCG_START_SCREEN_ONLY)
		{
			(int StateId, int GroupId, int GameId) tradingCardStartScreenHeader = GetTradingCardStartScreenHeader();
			int item2 = tradingCardStartScreenHeader.StateId;
			int item3 = tradingCardStartScreenHeader.GroupId;
			int item4 = tradingCardStartScreenHeader.GameId;
			(bool, bool, bool, bool) tradingCardStartScreenControlFlags = GetTradingCardStartScreenControlFlags();
			if (uiOnlyResponseForTcgLobby && IsExpectedTradingCardStartScreenRequest(result, item2) && TryHandleTradingCardStartScreenGoRequest(connection, worldTunnel, playerGuid, reason, result, result2, result3, text, num, item2, item3, item4))
			{
				return true;
			}
			if (!uiOnlyResponseForTcgLobby && IsExpectedTradingCardStartScreenRequest(result, item2) && TryHandleTradingCardPostGoAssetsReadyRequest(connection, worldTunnel, playerGuid, reason, result, result2, result3, text, num, item2))
			{
				return true;
			}
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressSubtype5", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("miniGameInstanceId", result), MinigameDiagnosticsLog.Field("requestArgA", result2), MinigameDiagnosticsLog.Field("requestArgB", result3), MinigameDiagnosticsLog.Field("headerExperiment", "row41"), MinigameDiagnosticsLog.Field("flagExperiment", "baseline_all_false"), MinigameDiagnosticsLog.Field("expectedStateId", item2), MinigameDiagnosticsLog.Field("expectedGroupId", item3), MinigameDiagnosticsLog.Field("expectedGameId", item4), MinigameDiagnosticsLog.Field("subtype5EchoMatchesStateId", result == item2), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("unknown16_0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown17_0x26b", tradingCardStartScreenControlFlags.Item2), MinigameDiagnosticsLog.Field("unknown18_0x26c", tradingCardStartScreenControlFlags.Item3), MinigameDiagnosticsLog.Field("unknown19_0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("showGateExpectedOpen", !tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("suppressedPackets", "MiniGameCreateGameResultPacket,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,HUD:showMinigame"), MinigameDiagnosticsLog.Field("allowedPackets", "none before the delayed MinigameStartHandler:setReady transition and GO click"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressRequestStartGameResponse", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("miniGameInstanceId", result), MinigameDiagnosticsLog.Field("requestArgA", result2), MinigameDiagnosticsLog.Field("requestArgB", result3), MinigameDiagnosticsLog.Field("headerExperiment", "row41"), MinigameDiagnosticsLog.Field("flagExperiment", "baseline_all_false"), MinigameDiagnosticsLog.Field("subtype5EchoMatchesStateId", result == item2), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("unknown16_0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown19_0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("showGateExpectedOpen", !tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("suppressedPackets", "MiniGameCreateGameResultPacket,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,HUD:showMinigame"), MinigameDiagnosticsLog.Field("allowedPackets", "delayed MinigameStartHandler:setReady first, then next TCG flow only after post-ready subtype5/GO"), MinigameDiagnosticsLog.Field("didLaunch", false));
			return true;
		}
		SendTradingCardStartGame(connection, worldTunnel, playerGuid, num);
		SendTradingCardGameStarted(connection, worldTunnel, playerGuid);
		SendTradingCardGameReady(connection, worldTunnel, playerGuid);
		return true;
	}

	private static bool HandleRequestTcgChallenge(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		ulong result = 0uL;
		reader.TryRead(out result);
		MinigameDiagnosticsLog.Info("tcg-challenge-request", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("target", result), MinigameDiagnosticsLog.Field("activity", 7));
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)40);
		packetWriter.Write(playerGuid);
		packetWriter.Write(result);
		packetWriter.Write(7);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("TradingCardStartGame from challenge. Player={player}", playerGuid);
		MinigameDiagnosticsLog.Info("response-sent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("response", "TradingCardStartGameFromChallenge"), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool HandleTradingCardStartGame(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		if (TCG_START_SCREEN_ONLY)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "HandleTradingCardStartGame"), MinigameDiagnosticsLog.Field("clientRequestType", "MiniGameStartGame"), MinigameDiagnosticsLog.Field("suppressedPackets", "MiniGameCreateGameResultPacket during explicit start request"), MinigameDiagnosticsLog.Field("didLaunch", false));
			return true;
		}
		SendTradingCardGameReady(connection, worldTunnel, playerGuid);
		_logger.LogInformation("TradingCardStartGameResponse. Player={player}", playerGuid);
		return true;
	}

	public static void SendTradingCardJoinGame(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		SendTradingCardJoinGame(connection, worldTunnel, playerGuid, allowStartScreenStateOnly: false, ResolveTradingCardStartScreenDisplayMetadata(41));
	}

	private static void SendTradingCardJoinGame(GatewayConnection connection, bool worldTunnel, ulong playerGuid, bool allowStartScreenStateOnly)
	{
		SendTradingCardJoinGame(connection, worldTunnel, playerGuid, allowStartScreenStateOnly, ResolveTradingCardStartScreenDisplayMetadata(41));
	}

	private static void SendTradingCardJoinGame(GatewayConnection connection, bool worldTunnel, ulong playerGuid, bool allowStartScreenStateOnly, TradingCardStartScreenDisplayMetadata displayMetadata)
	{
		if (TCG_START_SCREEN_ONLY && !allowStartScreenStateOnly)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "SendTradingCardJoinGame"), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:JoinGame"), MinigameDiagnosticsLog.Field("allowStartScreenStateOnlyRequested", allowStartScreenStateOnly), MinigameDiagnosticsLog.Field("suppressionReason", "JoinGame is disabled in start-screen-only mode after the 2026-06-02 black-screen test proved it transitions too far out of the normal world/UI state."), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		using PacketWriter packetWriter = new PacketWriter();
		int num;
		int num2;
		int num3;
		if (allowStartScreenStateOnly)
		{
			(num, num2, num3) = GetTradingCardStartScreenHeader();
		}
		else
		{
			num = 0;
			num2 = 0;
			num3 = 0;
		}
		packetWriter.Write((short)39);
		packetWriter.Write((byte)16);
		packetWriter.Write(num);
		packetWriter.Write(num2);
		packetWriter.Write(num3);
		WriteTradingCardMiniGameData(packetWriter, displayMetadata);
		int num4 = 0;
		packetWriter.Write(num4);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("TradingCardJoinGame. Player={player}", playerGuid);
		if (allowStartScreenStateOnly)
		{
			(bool, bool, bool, bool) tradingCardStartScreenControlFlags = GetTradingCardStartScreenControlFlags();
			MinigameDiagnosticsLog.Info("TcgControlledJoinGameHeaderExperiment", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("headerExperiment", "row41"), MinigameDiagnosticsLog.Field("flagExperiment", "baseline_all_false"), MinigameDiagnosticsLog.Field("stateId", num), MinigameDiagnosticsLog.Field("groupId", num2), MinigameDiagnosticsLog.Field("gameId", num3), MinigameDiagnosticsLog.Field("trailingFlag", num4), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("unknown16_0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown17_0x26b", tradingCardStartScreenControlFlags.Item2), MinigameDiagnosticsLog.Field("unknown18_0x26c", tradingCardStartScreenControlFlags.Item3), MinigameDiagnosticsLog.Field("unknown19_0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("expectedSubtype5EchoMiniGameInstanceId", num), MinigameDiagnosticsLog.Field("expectedPlayerStartMiniGame", "client-internal MiniGame:PlayerStartMiniGame if subtype16 reaches FUN_009beb70"), MinigameDiagnosticsLog.Field("expectedHandlerMiniGameStart", "setNotReady/show if MiniGameInfo+0x267=true and MiniGameInfo+0x26d=false"), MinigameDiagnosticsLog.Field("suppressedPackets", "RequestStartGame/subtype5 response,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal,FreeRealmsTCG.dll launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgMiniGameInfoControlFlagExperiment", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("flagExperiment", "baseline_all_false"), MinigameDiagnosticsLog.Field("knownUnsafeFlagExperiments", "unknown16_true caused black screen on 2026-06-03 and was reverted"), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("unknown16_0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown17_0x26b", tradingCardStartScreenControlFlags.Item2), MinigameDiagnosticsLog.Field("unknown18_0x26c", tradingCardStartScreenControlFlags.Item3), MinigameDiagnosticsLog.Field("unknown19_0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("nativeEvidence0x267", "FUN_009bfdb0 passes MiniGameInfo+0x267 as FUN_009beb70 param_4; FUN_009beb70 shows HandlerMiniGameStart only when this is true."), MinigameDiagnosticsLog.Field("nativeEvidence0x26a", "FUN_009beb70 lines 54-59 copy MiniGameInfo+0x26a into manager flag bit 0 before HandlerMiniGameStart"), MinigameDiagnosticsLog.Field("nativeEvidence0x26d", "FUN_009beb70 lines 82-86 only calls HandlerMiniGameStart:show when param_4 != 0 and MiniGameInfo+0x26d == false"), MinigameDiagnosticsLog.Field("showGateExpectedOpen", !tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("bodyOrderChanged", false), MinigameDiagnosticsLog.Field("envelopeChanged", false), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgControlledJoinGameStartScreenProbe", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("nativeClientEvent", "MiniGame:JoinGame"), MinigameDiagnosticsLog.Field("family", 39), MinigameDiagnosticsLog.Field("subtype", 16), MinigameDiagnosticsLog.Field("stateId", num), MinigameDiagnosticsLog.Field("groupId", num2), MinigameDiagnosticsLog.Field("gameId", num3), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("joinGameTrailingFlag", num4), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("nativeParam4Expected", true), MinigameDiagnosticsLog.Field("miniGameInfoOrder", "NameId,IconId,DescriptionId,Difficulty,ProfileType,Type,MembersOnly"), MinigameDiagnosticsLog.Field("wireOrder", "baseMiniGamePacket(family=39,subtype,header1,header2,header3),miniGameInfoData(nameToken,iconId,descriptionToken,difficulty,profileType,type,membersOnly,resources,objectiveCount,flags,unknownString,unknown14,unknown15,preselectedGameId/displayRowId,unknown16,unknown17,unknown18,unknown19,leaderboardId/displayRowId)"), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.RequestedRowId), MinigameDiagnosticsLog.Field("startScreenRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("startScreenTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("startScreenDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("originalNameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("nameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("miniGameInfoNameTokenWritten", displayMetadata.MiniGameInfoNameId), MinigameDiagnosticsLog.Field("iconId", displayMetadata.IconId), MinigameDiagnosticsLog.Field("originalDescriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("descriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("miniGameInfoDescriptionTokenWritten", displayMetadata.MiniGameInfoDescriptionId), MinigameDiagnosticsLog.Field("difficulty", displayMetadata.Difficulty), MinigameDiagnosticsLog.Field("profileType", 0), MinigameDiagnosticsLog.Field("miniGameType", 16), MinigameDiagnosticsLog.Field("membersOnly", false), MinigameDiagnosticsLog.Field("preselectedGameId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("miniGameInfoColumn11", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("miniGameInfoColumn16", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("unknown16_0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown17_0x26b", tradingCardStartScreenControlFlags.Item2), MinigameDiagnosticsLog.Field("unknown18_0x26c", tradingCardStartScreenControlFlags.Item3), MinigameDiagnosticsLog.Field("unknown19_0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("targetDatasource", "BaseClient.MiniGame"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
			MinigameDiagnosticsLog.Info("TcgStartScreenNativeShowExpected", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "controlled-subtype16-join-game-opener"), MinigameDiagnosticsLog.Field("headerExperiment", "row41"), MinigameDiagnosticsLog.Field("flagExperiment", "baseline_all_false"), MinigameDiagnosticsLog.Field("stateId", num), MinigameDiagnosticsLog.Field("groupId", num2), MinigameDiagnosticsLog.Field("gameId", num3), MinigameDiagnosticsLog.Field("decompileBranch", "FUN_009beb70: if(MiniGameInfo+0x267 != 0 && minigameFlag_0x26d == 0) HandlerMiniGameStart:setNotReady/show"), MinigameDiagnosticsLog.Field("joinGameTrailingFlag", num4), MinigameDiagnosticsLog.Field("showGate_0x267", true), MinigameDiagnosticsLog.Field("unknown16ManagerFlag0x26a", tradingCardStartScreenControlFlags.Item1), MinigameDiagnosticsLog.Field("unknown19ExpectedMinigameFlag0x26d", tradingCardStartScreenControlFlags.Item4), MinigameDiagnosticsLog.Field("expectedNativeCommands", "HandlerMiniGameStart:setNotReady,HandlerMiniGameStart:show"), MinigameDiagnosticsLog.Field("blockedFollowups", "RequestStartGame/subtype5 response,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("didLaunch", false));
		}
		else
		{
			MinigameDiagnosticsLog.Info("MiniGameJoinGamePacketSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("nativeClientEvent", "MiniGame:JoinGame"), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.RequestedRowId), MinigameDiagnosticsLog.Field("startScreenRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("startScreenTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("startScreenDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("miniGameType", 16), MinigameDiagnosticsLog.Field("originalMiniGameDataNameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("miniGameDataNameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("originalMiniGameDataDescriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("miniGameDataDescriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("miniGameDataIconId", displayMetadata.IconId), MinigameDiagnosticsLog.Field("targetDatasource", "BaseClient.MiniGame"), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		}
	}

	private static (int StateId, int GroupId, int GameId) GetTradingCardStartScreenHeader()
	{
		return (StateId: 41, GroupId: -1, GameId: -1);
	}

	private static (bool Unknown16, bool Unknown17, bool Unknown18, bool Unknown19) GetTradingCardStartScreenControlFlags()
	{
		return (Unknown16: false, Unknown17: false, Unknown18: false, Unknown19: false);
	}

	public static bool IsTradingCardStartScreenDisplayRow(int selectedRowId)
	{
		if (selectedRowId != 41 && !IsPacketReadyTradingCardStartScreenRow(selectedRowId) && selectedRowId != 603 && selectedRowId != 604)
		{
			return selectedRowId == 602;
		}
		return true;
	}

	private static bool IsExpectedTradingCardStartScreenRequest(int miniGameInstanceId, int expectedStateId)
	{
		if (miniGameInstanceId != expectedStateId)
		{
			return IsTradingCardStartScreenDisplayRow(miniGameInstanceId);
		}
		return true;
	}

	public static int ResolveTradingCardStartScreenDisplayRowId(int selectedRowId)
	{
		return ResolveTradingCardStartScreenDisplayMetadata(selectedRowId).DisplayRowId;
	}

	public static (int StateId, int GroupId, int GameId) GetTradingCardStartScreenHeaderForDiagnostics()
	{
		return GetTradingCardStartScreenHeader();
	}

	public static (int DisplayRowId, string Title, string Description) ResolveTradingCardStartScreenDisplayForDiagnostics(int selectedRowId)
	{
		TradingCardStartScreenDisplayMetadata tradingCardStartScreenDisplayMetadata = ResolveTradingCardStartScreenDisplayMetadata(selectedRowId);
		return (DisplayRowId: tradingCardStartScreenDisplayMetadata.DisplayRowId, Title: tradingCardStartScreenDisplayMetadata.Title, Description: tradingCardStartScreenDisplayMetadata.Description);
	}

	public static (int DisplayRowId, int NameId, int DescriptionId, int IconId, int Difficulty, string Title, string Description) ResolveTradingCardStartScreenLaunchMetadataForDiagnostics(int selectedRowId)
	{
		TradingCardStartScreenDisplayMetadata tradingCardStartScreenDisplayMetadata = ResolveTradingCardStartScreenDisplayMetadata(selectedRowId);
		return (DisplayRowId: tradingCardStartScreenDisplayMetadata.DisplayRowId, NameId: tradingCardStartScreenDisplayMetadata.MiniGameInfoNameId, DescriptionId: tradingCardStartScreenDisplayMetadata.MiniGameInfoDescriptionId, IconId: tradingCardStartScreenDisplayMetadata.IconId, Difficulty: tradingCardStartScreenDisplayMetadata.Difficulty, Title: tradingCardStartScreenDisplayMetadata.Title, Description: tradingCardStartScreenDisplayMetadata.Description);
	}

	private static TradingCardStartScreenDisplayMetadata ResolveTradingCardStartScreenDisplayMetadata(int selectedRowId)
	{
		if (TryResolvePacketReadyTradingCardStartScreenMetadata(selectedRowId, out var metadata))
		{
			return metadata;
		}
		return selectedRowId switch
		{
			603 => CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 500, 92731, 92763, 7591, 1, "Bry's Tournament Deck", "As a Card Duelist; Beat Bry's Tournament Deck", "brys_tournament_detail.dds"), 
			604 => CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 492, 92728, 92760, 7591, 1, "Bry's Trick Deck", "Play and beat Bry's Trick Deck. Bry can be found in Lakeshore.", "brys_trick_detail.dds"), 
			602 => CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 1049, 102280, 102281, 7591, 1, "Free Realms Trading Card Game Tutorial", "Learn how to play the Free Realms Trading Card Game.", "tcg_tutorial_detail.dds"), 
			_ => CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 41, 92784, 92785, 7591, 1, "Free Realms Trading Card Game Lobby", "Enter the TCG and you can learn to play, practice against Kai in a single-player match, or take on other Card Duelists! You can also check out your collection, trade with other players, or build your own decks!", "tcg_lobby_detail.dds", selectedRowId == 41), 
		};
	}

	private static bool IsPacketReadyTradingCardStartScreenRow(int selectedRowId)
	{
		if (selectedRowId != 7 && selectedRowId != 490 && selectedRowId != 498 && selectedRowId != 491 && selectedRowId != 499 && selectedRowId != 492 && selectedRowId != 500 && selectedRowId != 494 && selectedRowId != 502 && selectedRowId != 495 && selectedRowId != 503 && selectedRowId != 496 && selectedRowId != 504 && selectedRowId != 497 && selectedRowId != 505)
		{
			return selectedRowId == 1049;
		}
		return true;
	}

	private static bool TryResolvePacketReadyTradingCardStartScreenMetadata(int selectedRowId, out TradingCardStartScreenDisplayMetadata metadata)
	{
		TradingCardStartScreenDisplayMetadata tradingCardStartScreenDisplayMetadata;
		switch (selectedRowId)
		{
		case 7:
		case 41:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 41, 92784, 92785, 7591, 1, "Free Realms Trading Card Game Lobby", "Enter the TCG and you can learn to play, practice against Kai in a single-player match, or take on other Card Duelists! You can also check out your collection, trade with other players, or build your own decks!", "tcg_lobby_detail.dds");
			break;
		case 490:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 490, 92720, 92752, 7591, 1, "Sam's Trick Deck", "Play and beat Sam's Trick Deck.", "sams_trick_detail.dds");
			break;
		case 498:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 498, 92723, 92755, 7591, 1, "Sam's Tournament Deck", "As a Card Duelist, beat Sam's Tournament Deck.", "sams_tournament_detail.dds");
			break;
		case 491:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 491, 92724, 92756, 7591, 1, "Poe's Trick Deck", "Play and beat Poe's Trick Deck.", "poes_trick_detail.dds");
			break;
		case 499:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 499, 92727, 92759, 7591, 1, "Poe's Tournament Deck", "As a Card Duelist, beat Poe's Tournament Deck.", "poes_tournament_detail.dds");
			break;
		case 492:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 492, 92728, 92760, 7591, 1, "Bry's Trick Deck", "Play and beat Bry's Trick Deck. Bry can be found in Lakeshore.", "brys_trick_detail.dds");
			break;
		case 500:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 500, 92731, 92763, 7591, 1, "Bry's Tournament Deck", "As a Card Duelist; Beat Bry's Tournament Deck", "brys_tournament_detail.dds");
			break;
		case 494:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 494, 92736, 92768, 7591, 2, "Shifty's Trick Deck", "Play and beat Shifty's Trick Deck.", "shiftys_trick_detail.dds");
			break;
		case 502:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 502, 92739, 92771, 7591, 2, "Shifty's Tournament Deck", "As a Card Duelist, beat Shifty's Tournament Deck.", "shiftys_tournament_detail.dds");
			break;
		case 495:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 495, 92740, 92772, 7591, 2, "Jammi's Trick Deck", "Play and beat Jammi's Trick Deck.", "jammis_trick_detail.dds");
			break;
		case 503:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 503, 92743, 92775, 7591, 2, "Jammi's Tournament Deck", "As a Card Duelist, beat Jammi's Tournament Deck.", "jammis_tournament_detail.dds");
			break;
		case 496:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 496, 92744, 92776, 7591, 2, "Ari's Trick Deck", "Play and beat Ari's Trick Deck.", "aris_trick_detail.dds");
			break;
		case 504:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 504, 92747, 92779, 7591, 2, "Ari's Tournament Deck", "As a Card Duelist, beat Ari's Tournament Deck.", "aris_tournament_detail.dds");
			break;
		case 497:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 497, 92748, 92780, 7591, 2, "Maple's Trick Deck", "Play and beat Maple's Trick Deck.", "maples_trick_detail.dds");
			break;
		case 505:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 505, 92751, 92783, 7591, 2, "Maple's Tournament Deck", "As a Card Duelist, beat Maple's Tournament Deck.", "maples_tournament_detail.dds");
			break;
		case 1049:
			tradingCardStartScreenDisplayMetadata = CreatePacketReadyTradingCardStartScreenMetadata(selectedRowId, 1049, 102280, 102281, 7591, 1, "Free Realms Trading Card Game Tutorial", "Learn how to play the Free Realms Trading Card Game.", "tcg_tutorial_detail.dds");
			break;
		default:
			tradingCardStartScreenDisplayMetadata = default(TradingCardStartScreenDisplayMetadata);
			break;
		}
		metadata = tradingCardStartScreenDisplayMetadata;
		return metadata.RequestedKnownRow;
	}

	private static TradingCardStartScreenDisplayMetadata CreatePacketReadyTradingCardStartScreenMetadata(int requestedRowId, int displayRowId, int nameId, int descriptionId, int iconId, int difficulty, string title, string description, string detailImage, bool requestedKnownRow = true)
	{
		return new TradingCardStartScreenDisplayMetadata(requestedRowId, displayRowId, nameId, nameId, iconId, descriptionId, descriptionId, (difficulty <= 0) ? 1 : difficulty, ResolveOriginalLocaleStringForDiagnostics(nameId, title), ResolveOriginalLocaleStringForDiagnostics(descriptionId, description), detailImage, requestedKnownRow);
	}

	private static string ResolveOriginalLocaleStringForDiagnostics(int localeToken, string fallback)
	{
		TcgDetailUiPatch.LocaleLookupResult localeLookupResult = TcgDetailUiPatch.ResolveLocaleTokenForDiagnostics(localeToken);
		if (!localeLookupResult.Resolved || string.IsNullOrWhiteSpace(localeLookupResult.Text))
		{
			return fallback;
		}
		return localeLookupResult.Text;
	}

	private static void SendTradingCardMiniGameInfoSeed(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)68);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		WriteTradingCardMiniGameData(packetWriter);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("TcgStartScreenBaseClientMiniGameColumnMap", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("targetDatasource", "BaseClient.MiniGame"), MinigameDiagnosticsLog.Field("columnMap", "0=Name,1=IconID,2=Description,3=Difficulty,4=Type,7=MembersOnly,8=ProfileType,11=MiniGameId/SelectedDisplayRowId,16=LeaderboardId/SelectedDisplayRowId"), MinigameDiagnosticsLog.Field("decompileEvidence", "MiniGame_DS exposes columns 0-15 in ScriptsBase; MinigameStartScreen:GetLeaderboardId reads column 16, and native FUN_00c47160 reports 17 columns."), MinigameDiagnosticsLog.Field("didLaunch", false));
		MinigameDiagnosticsLog.Info("TcgStartScreenMiniGameInfoSeedSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("nativeClientEvent", "MiniGameInfoPacket"), MinigameDiagnosticsLog.Field("wireOrder", "baseMiniGamePacket(family=39,subtype,header1,header2,header3),miniGameInfoData(nameToken,iconId,descriptionToken,difficulty,profileType,type,membersOnly,resources,objectiveCount,flags,unknownString,unknown14,unknown15,preselectedGameId/displayRowId,unknown16,unknown17,unknown18,unknown19,leaderboardId/displayRowId)"), MinigameDiagnosticsLog.Field("fieldOrderFix", "subtype68 data now starts with BaseClient.MiniGame columns 0-4: name, icon, description, difficulty, type; native id 387 is not written as the first field"), MinigameDiagnosticsLog.Field("family", 39), MinigameDiagnosticsLog.Field("subtype", 68), MinigameDiagnosticsLog.Field("targetDatasource", "BaseClient.MiniGame"), MinigameDiagnosticsLog.Field("selectedRowId", 41), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("lobbyFacingMiniGameIdCandidate", 41), MinigameDiagnosticsLog.Field("nativeMiniGameIdCandidate", 387), MinigameDiagnosticsLog.Field("nativeDetailMiniGameIdCandidate", 727), MinigameDiagnosticsLog.Field("nameColumn", 0), MinigameDiagnosticsLog.Field("nameToken", 92784), MinigameDiagnosticsLog.Field("originalNameToken", 92784), MinigameDiagnosticsLog.Field("nameLiteral", "Free Realms Trading Card Game Lobby"), MinigameDiagnosticsLog.Field("iconColumn", 1), MinigameDiagnosticsLog.Field("iconId", 7591), MinigameDiagnosticsLog.Field("descriptionColumn", 2), MinigameDiagnosticsLog.Field("descriptionToken", 92785), MinigameDiagnosticsLog.Field("originalDescriptionToken", 92785), MinigameDiagnosticsLog.Field("descriptionLiteral", "Enter the TCG and you can learn to play, practice against Kai in a single-player match, or take on other Card Duelists! You can also check out your collection, trade with other players, or build your own decks!"), MinigameDiagnosticsLog.Field("difficultyColumn", 3), MinigameDiagnosticsLog.Field("difficulty", 1), MinigameDiagnosticsLog.Field("typeColumn", 4), MinigameDiagnosticsLog.Field("miniGameType", 16), MinigameDiagnosticsLog.Field("membersOnlyColumn", 7), MinigameDiagnosticsLog.Field("membersOnly", 0), MinigameDiagnosticsLog.Field("profileTypeColumn", 8), MinigameDiagnosticsLog.Field("profileType", 0), MinigameDiagnosticsLog.Field("miniGameIdColumn", 11), MinigameDiagnosticsLog.Field("leaderboardColumn", 16), MinigameDiagnosticsLog.Field("leaderboardId", 41), MinigameDiagnosticsLog.Field("miniGameInfoColumn11", 41), MinigameDiagnosticsLog.Field("miniGameInfoColumn16", 41), MinigameDiagnosticsLog.Field("suppressedPackets", "MiniGame:JoinGame,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	public static void SendTradingCardStartGame(GatewayConnection connection, bool worldTunnel, ulong playerGuid, ulong launchTicket)
	{
		SendTradingCardStartGame(connection, worldTunnel, playerGuid, launchTicket, allowStartScreenOnlyBypass: false, "SendTradingCardStartGame");
	}

	public static void SendTradingCardStartGameAfterStartScreenGo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, ulong launchTicket, string reason)
	{
		SendTradingCardStartGame(connection, worldTunnel, playerGuid, launchTicket, allowStartScreenOnlyBypass: true, reason);
	}

	private static void SendTradingCardStartGame(GatewayConnection connection, bool worldTunnel, ulong playerGuid, ulong launchTicket, bool allowStartScreenOnlyBypass, string reason)
	{
		if (TCG_START_SCREEN_ONLY && !allowStartScreenOnlyBypass)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:PlayerStartMiniGame"), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)40);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(launchTicket);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("TradingCardStartGame. Player={player}, LaunchTicket={launchTicket}", playerGuid, launchTicket);
		MinigameDiagnosticsLog.Info("MiniGamePlayerStartMiniGamePacketSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("nativeClientEvent", "MiniGame:PlayerStartMiniGame"), MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("miniGameInstanceId", 0), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("afterStartScreenGo", allowStartScreenOnlyBypass), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	public static void SendTradingCardGameStarted(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		SendTradingCardGameStarted(connection, worldTunnel, playerGuid, allowStartScreenOnlyBypass: false, "SendTradingCardGameStarted");
	}

	public static void SendTradingCardGameStartedAfterStartScreenGo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		var (stateId, groupId, gameId) = GetTradingCardStartScreenHeader();
		SendTradingCardGameStarted(connection, worldTunnel, playerGuid, allowStartScreenOnlyBypass: true, reason, stateId, groupId, gameId);
	}

	public static void SendTradingCardGameStartedAfterStartScreenGo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int stateId, int groupId, int gameId)
	{
		SendTradingCardGameStarted(connection, worldTunnel, playerGuid, allowStartScreenOnlyBypass: true, reason, stateId, groupId, gameId);
	}

	private static void SendTradingCardGameStarted(GatewayConnection connection, bool worldTunnel, ulong playerGuid, bool allowStartScreenOnlyBypass, string reason)
	{
		SendTradingCardGameStarted(connection, worldTunnel, playerGuid, allowStartScreenOnlyBypass, reason, 0, 0, 0);
	}

	private static void SendTradingCardGameStarted(GatewayConnection connection, bool worldTunnel, ulong playerGuid, bool allowStartScreenOnlyBypass, string reason, int stateId, int groupId, int gameId)
	{
		if (TCG_START_SCREEN_ONLY && !allowStartScreenOnlyBypass)
		{
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressBeginLoad", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:BeginLoad"), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)17);
		packetWriter.Write(stateId);
		packetWriter.Write(groupId);
		packetWriter.Write(gameId);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("TradingCardGameStarted. Player={player}", playerGuid);
		MinigameDiagnosticsLog.Info("MiniGameBeginLoadPacketSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("nativeClientEvent", "MiniGame:BeginLoad"), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("miniGameInstanceId", stateId), MinigameDiagnosticsLog.Field("groupId", groupId), MinigameDiagnosticsLog.Field("gameId", gameId), MinigameDiagnosticsLog.Field("afterStartScreenGo", allowStartScreenOnlyBypass), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	public static void SendTradingCardStartScreenBootstrap(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int selectedRowId, int currentCategoryId, string currentRowTitle, string reason)
	{
		TradingCardStartScreenDisplayMetadata displayMetadata = ResolveTradingCardStartScreenDisplayMetadata(selectedRowId);
		var (num, num2, num3) = GetTradingCardStartScreenHeader();
		if (!displayMetadata.RequestedKnownRow || !TcgDetailUiPatch.IsTcgDetailCategory(currentCategoryId))
		{
			MinigameDiagnosticsLog.Info("TcgStartScreenBootstrapSkipped", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("currentCategoryId", currentCategoryId), MinigameDiagnosticsLog.Field("currentRowTitle", currentRowTitle), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("skipReason", "Only known ungated TCG detail rows from category 11 can trigger the native start/loading screen bootstrap."));
			return;
		}
		MinigameDiagnosticsLog.Info("TcgStartScreenBootstrapRequested", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("selectedTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("selectedDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("currentCategoryId", currentCategoryId), MinigameDiagnosticsLog.Field("currentRowTitle", currentRowTitle), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("modalTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("modalDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
		MinigameDiagnosticsLog.Info("TcgStartScreenMetadataSelected", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("selectedTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("selectedDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenGroupId", num2), MinigameDiagnosticsLog.Field("startScreenGameId", num3), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("actualDisplayRowIdWritten", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("miniGameInfoColumn11", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("miniGameInfoColumn16", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("nameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("originalNameToken", displayMetadata.NameId), MinigameDiagnosticsLog.Field("actualNameTokenWritten", displayMetadata.MiniGameInfoNameId), MinigameDiagnosticsLog.Field("miniGameInfoNameTokenWritten", displayMetadata.MiniGameInfoNameId), MinigameDiagnosticsLog.Field("actualNameStringWritten", displayMetadata.Title), MinigameDiagnosticsLog.Field("descriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("originalDescriptionToken", displayMetadata.DescriptionId), MinigameDiagnosticsLog.Field("actualDescriptionTokenWritten", displayMetadata.MiniGameInfoDescriptionId), MinigameDiagnosticsLog.Field("miniGameInfoDescriptionTokenWritten", displayMetadata.MiniGameInfoDescriptionId), MinigameDiagnosticsLog.Field("actualDescriptionStringWritten", displayMetadata.Description), MinigameDiagnosticsLog.Field("metadataEncoding", "MiniGameInfo column 0/2 writes the selected packet-ready TCG row tokens; column 11/16 carry selected display row for SWF fallback"), MinigameDiagnosticsLog.Field("iconId", displayMetadata.IconId), MinigameDiagnosticsLog.Field("difficulty", displayMetadata.Difficulty), MinigameDiagnosticsLog.Field("detailImage", displayMetadata.DetailImage), MinigameDiagnosticsLog.Field("requestedKnownDisplayRow", displayMetadata.RequestedKnownRow), MinigameDiagnosticsLog.Field("metadataFallbackUsed", !displayMetadata.RequestedKnownRow), MinigameDiagnosticsLog.Field("metadataFallbackRowId", (!displayMetadata.RequestedKnownRow) ? 41 : displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("metadataFallbackReason", displayMetadata.RequestedKnownRow ? string.Empty : "selected row id is not a known ungated TCG start-screen display row"), MinigameDiagnosticsLog.Field("envelopeFrozen", true), MinigameDiagnosticsLog.Field("didLaunch", false));
		if (TCG_START_SCREEN_ONLY)
		{
			DateTime openedUtc = MarkTradingCardStartScreenOpened(playerGuid, displayMetadata.DisplayRowId);
			SendTradingCardJoinGame(connection, worldTunnel, playerGuid, allowStartScreenStateOnly: true, displayMetadata);
			SendMiniGameStartScreenSelectedDisplayRowCommand(connection, worldTunnel, playerGuid, reason + "-selected-display-row", displayMetadata.DisplayRowId);
			BeginTradingCardAssetsPreparation(connection, worldTunnel, playerGuid, reason, openedUtc, displayMetadata);
			MinigameDiagnosticsLog.Info("TcgStartScreenSuppressHudShow", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("suppressedPacket", "HUD:showMinigame"), MinigameDiagnosticsLog.Field("suppressedScripts", "HUD:showMinigame,MiniGameStateManager:startGame"), MinigameDiagnosticsLog.Field("didLaunch", false));
			MinigameDiagnosticsLog.Info("TcgStartScreenOnlyBootstrapPackets", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sentPackets", "controlled MiniGame:JoinGame/subtype16 with MiniGameInfo+0x267 show gate=true and trailing int preserved"), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("miniGameInfoOrder", "NameId,IconId,DescriptionId,Difficulty,ProfileType,Type,MembersOnly"), MinigameDiagnosticsLog.Field("suppressedPackets", "RequestStartGame/subtype5 launch until assets-ready and GO,HUD:showMinigame before GO,FreeRealmsTCG.dll direct launch before GO"), MinigameDiagnosticsLog.Field("diagnosticHypothesis", "Use the native JoinGame parser only far enough to open MinigameStartScreen in Loading; GO is gated by client-assets-ready, not by a fixed timer."), MinigameDiagnosticsLog.Field("directOpcode47CommandsSent", false), MinigameDiagnosticsLog.Field("registeredReadyEventScheduled", "after-client-assets-ready-only"), MinigameDiagnosticsLog.Field("readyDelayMs", 0), MinigameDiagnosticsLog.Field("readinessGate", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("didLaunch", false));
		}
		else
		{
			SendTradingCardMiniGameGroupInfo(connection, worldTunnel, playerGuid, reason + "-start-screen-group-info");
			SendTradingCardJoinGame(connection, worldTunnel, playerGuid);
			SendMiniGameStartScreenIntCommand(connection, worldTunnel, playerGuid, reason, "Show", default(int));
			SendMiniGameStartScreenIntCommand(connection, worldTunnel, playerGuid, reason, "SetLoading", 1);
			SendMiniGameStartHandlerCommand(connection, worldTunnel, playerGuid, reason, "show");
			SendMiniGameStartHandlerCommand(connection, worldTunnel, playerGuid, reason, "setNotReady");
		}
	}

	public static void TrackTradingCardStartScreenOpenedFromActivityLaunchPrelude(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int launchActivityId, int selectedRowId, string reason)
	{
		TradingCardStartScreenDisplayMetadata displayMetadata = ResolveTradingCardStartScreenDisplayMetadata(selectedRowId);
		int item = GetTradingCardStartScreenHeader().StateId;
		int num = ((launchActivityId > 0) ? launchActivityId : item);
		int num2 = -1;
		int num3 = -1;
		DateTime utcNow = DateTime.UtcNow;
		string text = TcgSessionRegistry.Register(playerGuid, num);
		TcgMatchmakingState.MarkLaunchSent(playerGuid, text);
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, text);
		MarkTradingCardStartScreenReadyFromSanctuaryPrelude(playerGuid, utcNow, displayMetadata, num, text, launchTicket);
		MinigameDiagnosticsLog.Info("TcgStartScreenSanctuaryPreludeOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sentPackets", "ClientActivityLaunch:InviteDetails,ClientActivityLaunch:ActivityLaunched,MiniGameInfoPacket"), MinigameDiagnosticsLog.Field("suppressedPacket", "row41 MiniGame:JoinGame/subtype16 bootstrap"), MinigameDiagnosticsLog.Field("suppressedLoadingPrep", true), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("startScreenStateId", num), MinigameDiagnosticsLog.Field("legacyHeaderStateId", item), MinigameDiagnosticsLog.Field("startScreenGroupId", num2), MinigameDiagnosticsLog.Field("startScreenGameId", num3), MinigameDiagnosticsLog.Field("startScreenDisplayRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("selectedTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("selectedDescription", displayMetadata.Description), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("typeId", 16), MinigameDiagnosticsLog.Field("canPlayerJoin", true), MinigameDiagnosticsLog.Field("membersOnly", false), MinigameDiagnosticsLog.Field("price", 0), MinigameDiagnosticsLog.Field("buyRequired", false), MinigameDiagnosticsLog.Field("locked", false), MinigameDiagnosticsLog.Field("usesPacketReadyActivity7", num == 7), MinigameDiagnosticsLog.Field("usesLegacyRow41", num == item || displayMetadata.DisplayRowId == 41), MinigameDiagnosticsLog.Field("readinessResultPacketSent", false), MinigameDiagnosticsLog.Field("directSwfSetLoadingFalseScheduled", false), MinigameDiagnosticsLog.Field("readinessGate", "native-client-type16-readiness-investigation; no fake UI ready packet, no subtype67, no subtype17, no BeginLoad"), MinigameDiagnosticsLog.Field("spinnerExpectedToClearAfter", "native client emits MiniGame:SetMiniGameReady when TCG type-16 state satisfies readiness conditions"), MinigameDiagnosticsLog.Field("actionStateExpectedAfterReady", "ready/actionable only after native readiness transition"), MinigameDiagnosticsLog.Field("ticket", text), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("serverFlowState", TcgStartScreenReadinessState.GoVisible.ToString()), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false), MinigameDiagnosticsLog.Field("didLaunch", false));
	}

	private static void MarkTradingCardStartScreenReadyFromSanctuaryPrelude(ulong playerGuid, DateTime openedUtc, TradingCardStartScreenDisplayMetadata displayMetadata, int stateId, string ticket, ulong launchTicket)
	{
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			orAdd.OpenedUtc = openedUtc;
			orAdd.PreparingStartedUtc = DateTime.MinValue;
			orAdd.WaitingForClientAssetsReadyUtc = DateTime.MinValue;
			orAdd.AssetsReadyUtc = openedUtc;
			orAdd.ReadyUtc = openedUtc;
			orAdd.GoVisibleUtc = openedUtc;
			orAdd.GoClickedUtc = DateTime.MinValue;
			orAdd.LaunchInProgressUtc = DateTime.MinValue;
			orAdd.LaunchCommandsSentUtc = DateTime.MinValue;
			orAdd.LaunchCompleteUtc = DateTime.MinValue;
			orAdd.ReadySent = true;
			orAdd.AssetsReadyResponseSent = true;
			orAdd.GoConfirmed = false;
			orAdd.LaunchStarted = false;
			orAdd.PreReadyRequestStartGameCount = 0;
			orAdd.SelectedRowId = displayMetadata.DisplayRowId;
			orAdd.MiniGameInstanceId = stateId;
			orAdd.Ticket = ticket ?? string.Empty;
			orAdd.LaunchTicket = launchTicket;
			orAdd.State = TcgStartScreenReadinessState.GoVisible;
		}
	}

	private static void SendTradingCardCreateGameResultStartScreenProbe(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		MinigameDiagnosticsLog.Info("TcgStartScreenRegisteredEventExperiment", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourcePacket", "MiniGameCreateGameResultPacket/subtype67"), MinigameDiagnosticsLog.Field("registeredEvent", "ActivityEvents:OnCreateGameResult"), MinigameDiagnosticsLog.Field("nativeEvidence", "FreeRealms.exe subtype 0x43 dispatches ActivityEvents:OnCreateGameResult when HasError=false."), MinigameDiagnosticsLog.Field("expectedEffect", "Let the native client event path decide whether to open or prime HandlerMiniGameStart after BaseClient.MiniGame has been seeded."), MinigameDiagnosticsLog.Field("blockedPackets", "MiniGame:JoinGame,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,HUD:showMinigame,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("directOpcode47CommandsSent", false), MinigameDiagnosticsLog.Field("didLaunch", false));
		SendTradingCardGameReady(connection, worldTunnel, playerGuid);
	}

	public static void SendTradingCardActivityUnlocks(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		int num = 0;
		num += SendTradingCardUnlockSet(connection, worldTunnel, playerGuid);
		if (worldTunnel)
		{
			num += SendTradingCardUnlockSet(connection, worldTunnel: false, playerGuid);
		}
		MinigameDiagnosticsLog.Info("TcgMinigameUnlockRowsSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedId", 0), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("nativeDetailMiniGameId", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "ListQueuesRequest"), MinigameDiagnosticsLog.Field("rows", num), MinigameDiagnosticsLog.Field("nativeUnlockedIds", 727.ToString()), MinigameDiagnosticsLog.Field("targetRowIdsSuppressed", string.Join(",", TradingCardActivityGameIds)), MinigameDiagnosticsLog.Field("poeCandidateRowIdsSuppressed", string.Join(",", TradingCardPoeCandidateGameIds)), MinigameDiagnosticsLog.Field("currentAliasIdsSuppressed", string.Join(",", TradingCardCarouselAliasIds)), MinigameDiagnosticsLog.Field("legacyAliasIdsSuppressed", string.Join(",", TradingCardLegacyCarouselAliasIds)), MinigameDiagnosticsLog.Field("targetRowUnlocksSuppressed", true), MinigameDiagnosticsLog.Field("poeUnlockSuppressedUntilGateKnown", true), MinigameDiagnosticsLog.Field("aliasUnlocksSuppressed", true), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("diagnosis", "Global MiniGameData unlocks feed Browser_V2's outer Games grid. Keep carousel/detail rows scoped to opcode 167 category data only; unlock only the native TCG row so detail thumbnails cannot become standalone Games tiles."));
	}

	private static int SendTradingCardUnlockSet(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		List<int> list = new List<int>();
		SendUniqueMiniGameDataUnlocked(connection, worldTunnel, playerGuid, 727, "native-detail-row", list);
		return list.Count;
	}

	private static void SendUniqueMiniGameDataUnlocked(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int gameId, string source, List<int> sent)
	{
		if (sent.Contains(gameId))
		{
			MinigameDiagnosticsLog.Info("minigame-unlock-skipped-duplicate", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("gameId", gameId), MinigameDiagnosticsLog.Field("source", source));
		}
		else
		{
			sent.Add(gameId);
			SendMiniGameDataUnlocked(connection, worldTunnel, playerGuid, gameId, source);
		}
	}

	public static void SendTradingCardMiniGameGroupInfo(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		TradingCardGroupElementRow[] visibleTradingCardGroupElements = GetVisibleTradingCardGroupElements(connection, worldTunnel, playerGuid, reason);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)50);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		WriteTradingCardMiniGameGroupInfo(packetWriter, playerGuid, worldTunnel, reason, visibleTradingCardGroupElements);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("TcgMiniGameGroupInfoSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", 727), MinigameDiagnosticsLog.Field("activityId", 24), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("groupNameIdSigned", 3388), MinigameDiagnosticsLog.Field("groupDescriptionIdSigned", 401571), MinigameDiagnosticsLog.Field("groupIconId", 7533), MinigameDiagnosticsLog.Field("preselectedGameId", 41), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("nativeNameId", 3388), MinigameDiagnosticsLog.Field("nativeDescriptionId", 401571), MinigameDiagnosticsLog.Field("nativeImageSetId", 7589), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "MiniGameGroupInfoBootstrap"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("elementCount", visibleTradingCardGroupElements.Length), MinigameDiagnosticsLog.Field("groupWireOrder", "baseMiniGameHeader0,baseMiniGameHeader1,baseMiniGameHeader2,groupId,groupNameToken,groupDescriptionToken,groupIconId,backgroundSwf,preselectedGameId,elementCount,elements,stageProgression,showStartScreenOnPlayNext,settingsIconId"), MinigameDiagnosticsLog.Field("elementWireOrder", "linkId,targetMiniGameDataId,detailImage,thumbnailImage,unlockedFlag,quantity,nameToken,descriptionToken,iconId,parentMiniGameDataId,membersOnlyFlag,mysteryChestIcon,levelString,difficulty,position,stageNumber,mustPurchaseItemId,priceItemId,enabledFlag"), MinigameDiagnosticsLog.Field("elementLinkIds", BuildTradingCardGroupElementLinkIdList(visibleTradingCardGroupElements)), MinigameDiagnosticsLog.Field("elementTargetMiniGameDataIds", BuildTradingCardGroupElementTargetIdList(visibleTradingCardGroupElements)), MinigameDiagnosticsLog.Field("elementNames", BuildTradingCardGroupElementNameList(visibleTradingCardGroupElements)), MinigameDiagnosticsLog.Field("poeTournamentGate", "disabled-unknown-unlock-condition"), MinigameDiagnosticsLog.Field("nativeSlotProof", "FUN_009b2c40 reads name into element[0xb], description into element[0xd], icon into element[0xe]; FUN_00c463a0 localizes those exact slots."), MinigameDiagnosticsLog.Field("rootCause", "The group element packet order stays unchanged. The row source now mirrors the opcode 167 default/gated carousel split, so Poe is not leaked into the group bootstrap while its unlock condition is unknown."), MinigameDiagnosticsLog.Field("fix", "Serialize each MiniGameGroup element as nameToken, descriptionToken, iconId, parentMiniGameDataId, then non-member/non-purchase flags, preserving the working image filenames."), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendMiniGameDataUnlocked(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int gameId, string source)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)2);
		packetWriter.Write(gameId);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("minigame-unlock-sent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("gameId", gameId), MinigameDiagnosticsLog.Field("source", source), MinigameDiagnosticsLog.Field("isTargetRow", Array.IndexOf(TradingCardActivityGameIds, gameId) >= 0), MinigameDiagnosticsLog.Field("isPoeCandidateRow", Array.IndexOf(TradingCardPoeCandidateGameIds, gameId) >= 0), MinigameDiagnosticsLog.Field("isCurrentAlias", Array.IndexOf(TradingCardCarouselAliasIds, gameId) >= 0), MinigameDiagnosticsLog.Field("isLegacyAlias", Array.IndexOf(TradingCardLegacyCarouselAliasIds, gameId) >= 0), MinigameDiagnosticsLog.Field("isNativeDetailRow", gameId == 727), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static bool IsTradingCardCarouselAliasId(int gameId)
	{
		if (Array.IndexOf(TradingCardCarouselAliasIds, gameId) < 0)
		{
			return Array.IndexOf(TradingCardLegacyCarouselAliasIds, gameId) >= 0;
		}
		return true;
	}

	private static bool IsTradingCardGroupRequest(int value)
	{
		if (!TcgDetailUiPatch.IsTcgDetailCategory(value) && value != 36 && value != 387 && value != 41 && Array.IndexOf(TradingCardActivityGameIds, value) < 0)
		{
			return IsTradingCardCarouselAliasId(value);
		}
		return true;
	}

	private static bool HandleCreateGameResult(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		MinigameDiagnosticsLog.Info("create-game-result", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		if (TCG_START_SCREEN_ONLY)
		{
			MinigameDiagnosticsLog.Info("TcgGameplayLaunchSuppressedForStartScreenOnly", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "HandleCreateGameResult"), MinigameDiagnosticsLog.Field("clientRequestType", "MiniGameCreateGameResult"), MinigameDiagnosticsLog.Field("suppressedPackets", "MiniGameCreateGameResultPacket echo"), MinigameDiagnosticsLog.Field("didLaunch", false));
			return true;
		}
		SendTradingCardGameReady(connection, worldTunnel, playerGuid);
		return true;
	}

	public static void SendTradingCardGameReady(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason = "tcg-game-ready", bool readinessOnly = false, int readinessMiniGameInstanceId = 0)
	{
		int num = ((!readinessOnly) ? 387 : ((readinessMiniGameInstanceId > 0) ? readinessMiniGameInstanceId : GetTradingCardStartScreenHeader().StateId));
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)67);
		packetWriter.Write(num);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write((byte)0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("TradingCardGameReady. Player={player}", playerGuid);
		MinigameDiagnosticsLog.Info("MiniGameCreateGameResultPacketSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("nativeClientEvent", "ActivityEvents:OnCreateGameResult"), MinigameDiagnosticsLog.Field("miniGameId", num), MinigameDiagnosticsLog.Field("nativeTcgMiniGameId", 387), MinigameDiagnosticsLog.Field("hasError", false), MinigameDiagnosticsLog.Field("readinessOnly", readinessOnly), MinigameDiagnosticsLog.Field("readinessStateSource", (!readinessOnly) ? "native-tcg" : ((readinessMiniGameInstanceId > 0) ? "active-start-screen" : "legacy-start-screen-header")), MinigameDiagnosticsLog.Field("expectedFollowup", readinessOnly ? "native ActivityEvents:OnCreateGameResult should release MinigameStartScreen Loading into GO for the active start-screen instance" : "native ActivityEvents:OnCreateGameResult acknowledges the already-started minigame flow"), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false), MinigameDiagnosticsLog.Field("rootCause", "Readiness-only MiniGameCreateGameResultPacket must match the active MiniGame start-screen state; packet-ready TCG uses activity/state 7 while row41 remains only the legacy fallback envelope."), MinigameDiagnosticsLog.Field("fix", "send sub opcode 67 with HasError=false and the active state-matched instance id"), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void WriteTradingCardMiniGameData(PacketWriter packetWriter)
	{
		WriteTradingCardMiniGameData(packetWriter, ResolveTradingCardStartScreenDisplayMetadata(41));
	}

	private static void WriteTradingCardMiniGameData(PacketWriter packetWriter, TradingCardStartScreenDisplayMetadata displayMetadata)
	{
		(bool, bool, bool, bool) tradingCardStartScreenControlFlags = GetTradingCardStartScreenControlFlags();
		packetWriter.Write(displayMetadata.MiniGameInfoNameId);
		packetWriter.Write(displayMetadata.IconId);
		packetWriter.Write(displayMetadata.MiniGameInfoDescriptionId);
		packetWriter.Write(displayMetadata.Difficulty);
		packetWriter.Write(0);
		packetWriter.Write(16);
		packetWriter.Write((byte)0);
		WriteEmptyMiniGameResource(packetWriter);
		WriteEmptyMiniGameResource(packetWriter);
		WriteEmptyMiniGameResource(packetWriter);
		packetWriter.Write(0);
		packetWriter.Write((byte)0);
		packetWriter.Write((byte)0);
		packetWriter.Write((byte)0);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)0);
		packetWriter.Write(string.Empty);
		packetWriter.Write(0);
		packetWriter.Write((byte)0);
		packetWriter.Write(displayMetadata.DisplayRowId);
		packetWriter.Write(tradingCardStartScreenControlFlags.Item1 ? ((byte)1) : ((byte)0));
		packetWriter.Write(tradingCardStartScreenControlFlags.Item2 ? ((byte)1) : ((byte)0));
		packetWriter.Write(tradingCardStartScreenControlFlags.Item3 ? ((byte)1) : ((byte)0));
		packetWriter.Write(tradingCardStartScreenControlFlags.Item4 ? ((byte)1) : ((byte)0));
		packetWriter.Write(displayMetadata.DisplayRowId);
	}

	private static void WriteEmptyMiniGameResource(PacketWriter packetWriter)
	{
		packetWriter.Write((byte)0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0L);
		packetWriter.Write(0L);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
	}

	private static DateTime MarkTradingCardStartScreenOpened(ulong playerGuid, int selectedRowId)
	{
		DateTime utcNow = DateTime.UtcNow;
		int item = GetTradingCardStartScreenHeader().StateId;
		PruneTcgStartScreenGoLaunchGuards(utcNow);
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlows[playerGuid] = new TcgStartScreenFlowState
			{
				OpenedUtc = utcNow,
				SelectedRowId = ResolveTradingCardStartScreenDisplayRowId(selectedRowId),
				MiniGameInstanceId = item,
				State = TcgStartScreenReadinessState.StartScreenOpened
			};
			return utcNow;
		}
	}

	private static void BeginTradingCardAssetsPreparation(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, DateTime openedUtc, TradingCardStartScreenDisplayMetadata displayMetadata, int startScreenStateId = 0)
	{
		int item = GetTradingCardStartScreenHeader().StateId;
		int stateId = ((startScreenStateId > 0) ? startScreenStateId : item);
		PruneTcgStartScreenGoLaunchGuards(openedUtc);
		if (!TcgMatchmakingState.TryGetRecentLaunchTicket(playerGuid, out var ticket))
		{
			ticket = TcgSessionRegistry.Register(playerGuid, stateId);
			TcgMatchmakingState.MarkLaunchSent(playerGuid, ticket);
		}
		ulong launchTicket = TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket);
		DateTime utcNow = DateTime.UtcNow;
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, stateId);
		TcgStartScreenGoLaunchState addValue = new TcgStartScreenGoLaunchState
		{
			OpenedUtc = openedUtc,
			PreparingStartedUtc = utcNow,
			MiniGameInstanceId = stateId,
			Ticket = (ticket ?? string.Empty),
			LaunchTicket = launchTicket
		};
		TcgStartScreenGoLaunchGuards.AddOrUpdate(tcgStartScreenGoLaunchGuardKey, addValue, delegate(string _, TcgStartScreenGoLaunchState existingState)
		{
			lock (existingState.Sync)
			{
				if (existingState.LaunchStartedUtc != DateTime.MinValue && utcNow - existingState.LaunchStartedUtc <= TcgStartScreenGoLaunchGuardTtl)
				{
					return existingState;
				}
				existingState.OpenedUtc = openedUtc;
				existingState.PreparingStartedUtc = utcNow;
				existingState.FirstGoUtc = DateTime.MinValue;
				existingState.LaunchStartedUtc = DateTime.MinValue;
				existingState.LaunchCompleteUtc = DateTime.MinValue;
				existingState.CreateMiniGameResponseUtc = DateTime.MinValue;
				existingState.AssetsReadyResponseUtc = DateTime.MinValue;
				existingState.MiniGameInstanceId = stateId;
				existingState.Ticket = ticket ?? string.Empty;
				existingState.LaunchTicket = launchTicket;
				return existingState;
			}
		});
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			orAdd.OpenedUtc = openedUtc;
			orAdd.PreparingStartedUtc = utcNow;
			orAdd.WaitingForClientAssetsReadyUtc = utcNow;
			orAdd.AssetsReadyUtc = DateTime.MinValue;
			orAdd.ReadyUtc = DateTime.MinValue;
			orAdd.GoVisibleUtc = DateTime.MinValue;
			orAdd.GoClickedUtc = DateTime.MinValue;
			orAdd.LaunchInProgressUtc = DateTime.MinValue;
			orAdd.LaunchCommandsSentUtc = DateTime.MinValue;
			orAdd.LaunchCompleteUtc = DateTime.MinValue;
			orAdd.ReadySent = false;
			orAdd.AssetsReadyResponseSent = false;
			orAdd.GoConfirmed = false;
			orAdd.LaunchStarted = false;
			orAdd.PreReadyRequestStartGameCount = 0;
			orAdd.SelectedRowId = displayMetadata.DisplayRowId;
			orAdd.MiniGameInstanceId = stateId;
			orAdd.Ticket = ticket ?? string.Empty;
			orAdd.LaunchTicket = launchTicket;
			orAdd.State = TcgStartScreenReadinessState.WaitingForClientAssetsReady;
		}
		MinigameDiagnosticsLog.Info("TcgStartScreenOpened", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.StartScreenOpened.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", stateId), MinigameDiagnosticsLog.Field("defaultLegacyStateId", item), MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("selectedTitle", displayMetadata.Title), MinigameDiagnosticsLog.Field("loadingState", true), MinigameDiagnosticsLog.Field("actionState", "not-ready-yet"), MinigameDiagnosticsLog.Field("didLaunch", false));
		MinigameDiagnosticsLog.Info("TcgAssetsPrepareStarted", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.PreparingTcgAssets.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", stateId), MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("didShowGo", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		ClientActivityLaunchPacketHandler.PrepareTcgLaunchBehindStartScreenLoading(connection, worldTunnel, playerGuid, ticket, displayMetadata.DisplayRowId);
		MinigameDiagnosticsLog.Info("TcgWaitingForClientAssetsReady", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.WaitingForClientAssetsReady.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", stateId), MinigameDiagnosticsLog.Field("selectedRowId", displayMetadata.DisplayRowId), MinigameDiagnosticsLog.Field("readinessGate", "client-assets-ready-request/subtype37"), MinigameDiagnosticsLog.Field("didShowGo", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		ScheduleTradingCardAssetsReadyTimeoutFallback(connection, worldTunnel, playerGuid, reason, openedUtc, ticket, launchTicket, stateId, displayMetadata.DisplayRowId);
	}

	private static void ScheduleTradingCardAssetsReadyTimeoutFallback(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, DateTime openedUtc, string ticket, ulong launchTicket, int miniGameInstanceId, int selectedRowId)
	{
		MinigameDiagnosticsLog.Info("TcgAssetsReadyTimeoutFallbackScheduled", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("delayMs", 2500), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("fallbackAction", "unlock GO only if no client-assets-ready request arrives"), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		Task.Run(async delegate
		{
			await Task.Delay(2500).ConfigureAwait(continueOnCapturedContext: false);
			TrySendTradingCardAssetsReadyTimeoutFallback(connection, worldTunnel, playerGuid, reason, openedUtc, ticket, launchTicket, miniGameInstanceId);
		});
	}

	private static void TrySendTradingCardAssetsReadyTimeoutFallback(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, DateTime openedUtc, string ticket, ulong launchTicket, int miniGameInstanceId)
	{
		DateTime utcNow = DateTime.UtcNow;
		int num = 41;
		double num2 = Math.Round((utcNow - openedUtc).TotalMilliseconds, 1);
		string value = string.Empty;
		lock (TcgStartScreenFlowSync)
		{
			if (!TcgStartScreenFlows.TryGetValue(playerGuid, out var value2))
			{
				value = "flow-missing";
			}
			else if (openedUtc != DateTime.MinValue && value2.OpenedUtc != DateTime.MinValue && value2.OpenedUtc != openedUtc)
			{
				value = "newer-start-screen-opened";
				num = ResolveTradingCardStartScreenDisplayRowId(value2.SelectedRowId);
			}
			else if (value2.AssetsReadyResponseSent)
			{
				value = "client-assets-ready-already-observed";
				num = ResolveTradingCardStartScreenDisplayRowId(value2.SelectedRowId);
			}
			else if (value2.ReadySent)
			{
				value = "ready-already-sent";
				num = ResolveTradingCardStartScreenDisplayRowId(value2.SelectedRowId);
			}
			else if (value2.GoConfirmed || value2.LaunchStarted)
			{
				value = "go-or-launch-already-started";
				num = ResolveTradingCardStartScreenDisplayRowId(value2.SelectedRowId);
			}
			else
			{
				num = ResolveTradingCardStartScreenDisplayRowId(value2.SelectedRowId);
				value2.AssetsReadyUtc = utcNow;
				value2.AssetsReadyResponseSent = true;
				value2.MiniGameInstanceId = miniGameInstanceId;
				value2.Ticket = ticket ?? string.Empty;
				value2.LaunchTicket = launchTicket;
				value2.State = TcgStartScreenReadinessState.AssetsReady;
			}
		}
		if (!string.IsNullOrEmpty(value))
		{
			MinigameDiagnosticsLog.Info("TcgAssetsReadyTimeoutFallbackSkipped", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("skipReason", value), MinigameDiagnosticsLog.Field("delayMs", 2500), MinigameDiagnosticsLog.Field("waitAgeMs", num2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
			return;
		}
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, miniGameInstanceId);
		TcgStartScreenGoLaunchState orAdd = TcgStartScreenGoLaunchGuards.GetOrAdd(tcgStartScreenGoLaunchGuardKey, (string _) => new TcgStartScreenGoLaunchState
		{
			OpenedUtc = openedUtc,
			PreparingStartedUtc = openedUtc,
			MiniGameInstanceId = miniGameInstanceId,
			Ticket = (ticket ?? string.Empty),
			LaunchTicket = launchTicket
		});
		lock (orAdd.Sync)
		{
			if (orAdd.AssetsReadyResponseUtc == DateTime.MinValue)
			{
				orAdd.AssetsReadyResponseUtc = utcNow;
			}
		}
		MinigameDiagnosticsLog.Info("TcgAssetsReadyTimeoutFallback", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("fallbackReason", "no client-assets-ready request arrived before timeout"), MinigameDiagnosticsLog.Field("delayMs", 2500), MinigameDiagnosticsLog.Field("waitAgeMs", num2), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.AssetsReady.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgClientAssetsReadyResponseSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "assets-ready-timeout-fallback"), MinigameDiagnosticsLog.Field("clientRequestType", "timeout-fallback/no-client-assets-ready-request"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("response", "readiness timeout accepted; launch commands deferred until GO"), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		SendTradingCardStartScreenReadyFromAssetsReady(connection, worldTunnel, playerGuid, "assets-ready-timeout-fallback", openedUtc, utcNow, ticket, launchTicket, miniGameInstanceId);
	}

	private static string GetTcgStartScreenGoLaunchGuardKey(ulong playerGuid, string ticket, ulong launchTicket, int miniGameInstanceId)
	{
		string value = (string.IsNullOrEmpty(ticket) ? launchTicket.ToString("X") : ticket);
		return $"{playerGuid}:{value}:{miniGameInstanceId}";
	}

	private static string BuildTcgLaunchCorrelationId(ulong playerGuid, string ticket, ulong launchTicket)
	{
		string value = (string.IsNullOrEmpty(ticket) ? "no-ticket" : ticket);
		return $"{playerGuid}:{value}:{launchTicket}";
	}

	public static bool ShouldSendTradingCardPostGoLaunchFollowup(ulong playerGuid, string ticket, ulong launchTicket, int miniGameInstanceId, out string skipReason, out string activeTicket, out ulong activeLaunchTicket, out int activeMiniGameInstanceId, out string activeState, out bool activeGoVisible, out bool activeLaunchStarted)
	{
		skipReason = string.Empty;
		activeTicket = string.Empty;
		activeLaunchTicket = 0uL;
		activeMiniGameInstanceId = 0;
		activeState = "none";
		activeGoVisible = false;
		activeLaunchStarted = false;
		lock (TcgStartScreenFlowSync)
		{
			if (!TcgStartScreenFlows.TryGetValue(playerGuid, out var value))
			{
				return true;
			}
			activeTicket = value.Ticket ?? string.Empty;
			activeLaunchTicket = value.LaunchTicket;
			activeMiniGameInstanceId = value.MiniGameInstanceId;
			activeState = value.State.ToString();
			activeGoVisible = value.ReadySent || value.GoVisibleUtc != DateTime.MinValue || value.State == TcgStartScreenReadinessState.GoVisible || value.State == TcgStartScreenReadinessState.GoClicked || value.State == TcgStartScreenReadinessState.LaunchInProgress || value.State == TcgStartScreenReadinessState.LaunchCommandsSent || value.State == TcgStartScreenReadinessState.AwaitingTcgDllLoad || value.State == TcgStartScreenReadinessState.AwaitingTcgServerConnect || value.State == TcgStartScreenReadinessState.TcgConnected || value.State == TcgStartScreenReadinessState.LaunchComplete;
			activeLaunchStarted = value.LaunchStarted || value.GoConfirmed || value.State == TcgStartScreenReadinessState.LaunchInProgress || value.State == TcgStartScreenReadinessState.LaunchCommandsSent || value.State == TcgStartScreenReadinessState.AwaitingTcgDllLoad || value.State == TcgStartScreenReadinessState.AwaitingTcgServerConnect || value.State == TcgStartScreenReadinessState.TcgConnected || value.State == TcgStartScreenReadinessState.LaunchComplete;
			if (!string.Equals(activeTicket, ticket ?? string.Empty, StringComparison.Ordinal) || (activeLaunchTicket != 0L && launchTicket != 0L && activeLaunchTicket != launchTicket) || (activeMiniGameInstanceId != 0 && miniGameInstanceId != 0 && activeMiniGameInstanceId != miniGameInstanceId))
			{
				skipReason = "stale-ticket-active-start-screen-session";
				return false;
			}
			if (!activeLaunchStarted)
			{
				skipReason = (activeGoVisible ? "go-visible-awaiting-user-click" : "start-screen-not-ready-for-launch");
				return false;
			}
			return true;
		}
	}

	public static bool TryConsumeTradingCardClientActivityLaunchGo(ulong playerGuid, out string ticket, out ulong launchTicket, out int miniGameInstanceId, out int selectedDisplayRowId, out string selectedTitle, out string selectedDescription, out string activeState, out bool activeGoVisible, out bool activeLaunchStarted, out string skipReason)
	{
		ticket = string.Empty;
		launchTicket = 0uL;
		miniGameInstanceId = 0;
		selectedDisplayRowId = 41;
		selectedTitle = string.Empty;
		selectedDescription = string.Empty;
		activeState = "none";
		activeGoVisible = false;
		activeLaunchStarted = false;
		skipReason = string.Empty;
		DateTime utcNow = DateTime.UtcNow;
		lock (TcgStartScreenFlowSync)
		{
			if (!TcgStartScreenFlows.TryGetValue(playerGuid, out var value))
			{
				skipReason = "no-active-start-screen";
				return false;
			}
			activeState = value.State.ToString();
			activeGoVisible = value.ReadySent || value.GoVisibleUtc != DateTime.MinValue || value.State == TcgStartScreenReadinessState.GoVisible || value.State == TcgStartScreenReadinessState.GoClicked || value.State == TcgStartScreenReadinessState.LaunchInProgress || value.State == TcgStartScreenReadinessState.LaunchCommandsSent || value.State == TcgStartScreenReadinessState.AwaitingTcgDllLoad || value.State == TcgStartScreenReadinessState.AwaitingTcgServerConnect || value.State == TcgStartScreenReadinessState.TcgConnected || value.State == TcgStartScreenReadinessState.LaunchComplete;
			activeLaunchStarted = value.LaunchStarted || value.GoConfirmed || value.State == TcgStartScreenReadinessState.LaunchInProgress || value.State == TcgStartScreenReadinessState.LaunchCommandsSent || value.State == TcgStartScreenReadinessState.AwaitingTcgDllLoad || value.State == TcgStartScreenReadinessState.AwaitingTcgServerConnect || value.State == TcgStartScreenReadinessState.TcgConnected || value.State == TcgStartScreenReadinessState.LaunchComplete;
			if (!activeGoVisible)
			{
				skipReason = "start-screen-not-ready-for-launch";
				return false;
			}
			if (activeLaunchStarted)
			{
				skipReason = "start-screen-launch-already-started";
				return false;
			}
			miniGameInstanceId = ((value.MiniGameInstanceId > 0) ? value.MiniGameInstanceId : 7);
			selectedDisplayRowId = ResolveTradingCardStartScreenDisplayRowId(value.SelectedRowId);
			ticket = value.Ticket ?? string.Empty;
			if (string.IsNullOrEmpty(ticket))
			{
				ticket = TcgSessionRegistry.Register(playerGuid, miniGameInstanceId);
				value.Ticket = ticket;
			}
			launchTicket = ((value.LaunchTicket != 0L) ? value.LaunchTicket : TcgSessionRegistry.GetLaunchTicket(playerGuid, ticket));
			value.LaunchTicket = launchTicket;
			value.GoConfirmed = true;
			value.GoClickedUtc = utcNow;
			value.LaunchInProgressUtc = utcNow;
			value.LaunchStarted = true;
			value.State = TcgStartScreenReadinessState.LaunchInProgress;
			activeLaunchStarted = true;
			activeState = value.State.ToString();
		}
		TradingCardStartScreenDisplayMetadata tradingCardStartScreenDisplayMetadata = ResolveTradingCardStartScreenDisplayMetadata(selectedDisplayRowId);
		selectedTitle = tradingCardStartScreenDisplayMetadata.Title;
		selectedDescription = tradingCardStartScreenDisplayMetadata.Description;
		string guardTicket = ticket ?? string.Empty;
		ulong guardLaunchTicket = launchTicket;
		int guardMiniGameInstanceId = miniGameInstanceId;
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, guardTicket, guardLaunchTicket, guardMiniGameInstanceId);
		TcgStartScreenGoLaunchState addValue = new TcgStartScreenGoLaunchState
		{
			OpenedUtc = utcNow,
			PreparingStartedUtc = utcNow,
			FirstGoUtc = utcNow,
			LaunchStartedUtc = utcNow,
			MiniGameInstanceId = guardMiniGameInstanceId,
			Ticket = guardTicket,
			LaunchTicket = guardLaunchTicket
		};
		TcgStartScreenGoLaunchGuards.AddOrUpdate(tcgStartScreenGoLaunchGuardKey, addValue, delegate(string _, TcgStartScreenGoLaunchState existingState)
		{
			lock (existingState.Sync)
			{
				existingState.FirstGoUtc = ((existingState.FirstGoUtc == DateTime.MinValue) ? utcNow : existingState.FirstGoUtc);
				existingState.LaunchStartedUtc = ((existingState.LaunchStartedUtc == DateTime.MinValue) ? utcNow : existingState.LaunchStartedUtc);
				existingState.MiniGameInstanceId = guardMiniGameInstanceId;
				existingState.Ticket = guardTicket;
				existingState.LaunchTicket = guardLaunchTicket;
				return existingState;
			}
		});
		return true;
	}

	public static void MarkTradingCardClientActivityLaunchGoCommandsSent(ulong playerGuid, string ticket, ulong launchTicket, int miniGameInstanceId)
	{
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, miniGameInstanceId);
		if (TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey, out var value))
		{
			lock (value.Sync)
			{
				value.LaunchCommandsSentUtc = DateTime.UtcNow;
			}
		}
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			orAdd.LaunchCommandsSentUtc = DateTime.UtcNow;
			orAdd.State = TcgStartScreenReadinessState.LaunchCommandsSent;
		}
	}

	private static void PruneTcgStartScreenGoLaunchGuards(DateTime utcNow)
	{
		foreach (KeyValuePair<string, TcgStartScreenGoLaunchState> tcgStartScreenGoLaunchGuard in TcgStartScreenGoLaunchGuards)
		{
			DateTime dateTime = ((tcgStartScreenGoLaunchGuard.Value.FirstGoUtc != DateTime.MinValue) ? tcgStartScreenGoLaunchGuard.Value.FirstGoUtc : ((tcgStartScreenGoLaunchGuard.Value.OpenedUtc != DateTime.MinValue) ? tcgStartScreenGoLaunchGuard.Value.OpenedUtc : tcgStartScreenGoLaunchGuard.Value.PreparingStartedUtc));
			if (dateTime != DateTime.MinValue && utcNow - dateTime > TcgStartScreenGoLaunchGuardTtl)
			{
				TcgStartScreenGoLaunchGuards.TryRemove(tcgStartScreenGoLaunchGuard.Key, out var _);
			}
		}
	}

	private static bool TryHandleTradingCardPostGoCreateMiniGameRequest(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int requestedGameId, int requestArgA, int requestArgB, string ticket, ulong launchTicket)
	{
		if (string.IsNullOrEmpty(ticket))
		{
			return false;
		}
		int item = GetTradingCardStartScreenHeader().StateId;
		int num = item;
		lock (TcgStartScreenFlowSync)
		{
			if (TcgStartScreenFlows.TryGetValue(playerGuid, out var value) && string.Equals(value.Ticket ?? string.Empty, ticket ?? string.Empty, StringComparison.Ordinal) && (value.LaunchTicket == 0L || launchTicket == 0L || value.LaunchTicket == launchTicket) && value.MiniGameInstanceId > 0)
			{
				num = value.MiniGameInstanceId;
			}
		}
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, num);
		string value2 = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
		if (!TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey, out var value3))
		{
			string tcgStartScreenGoLaunchGuardKey2 = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, item);
			if (item == num || !TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey2, out value3))
			{
				MinigameDiagnosticsLog.Info("TcgPostGoCreateMiniGameGuardMissing", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-create-minigame"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("expectedStartScreenStateId", num), MinigameDiagnosticsLog.Field("legacyExpectedStateId", item), MinigameDiagnosticsLog.Field("suppressedFallback", "row41 bootstrap not sent while waiting for matching post-GO guard"), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
				return true;
			}
		}
		DateTime utcNow = DateTime.UtcNow;
		if (value3.LaunchStartedUtc == DateTime.MinValue)
		{
			MinigameDiagnosticsLog.Info("TcgPostGoCreateMiniGamePreGoSuppressed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "pre-go-create-minigame"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("miniGameInstanceId", num), MinigameDiagnosticsLog.Field("state", "WaitingForClientAssetsReady"), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:JoinGame/subtype16 start-screen reopen"), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
			return true;
		}
		double num2 = Math.Round((utcNow - value3.LaunchStartedUtc).TotalMilliseconds, 1);
		lock (value3.Sync)
		{
			if (value3.CreateMiniGameResponseUtc != DateTime.MinValue)
			{
				double num3 = Math.Round((utcNow - value3.CreateMiniGameResponseUtc).TotalMilliseconds, 1);
				MinigameDiagnosticsLog.Info("TcgPostGoCreateMiniGameDuplicateSuppressed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-direct-start"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("goAgeMs", num2), MinigameDiagnosticsLog.Field("duplicateAgeMs", num3), MinigameDiagnosticsLog.Field("alreadyStarted", true), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:JoinGame/subtype16 start-screen opener"), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunch", false));
				return true;
			}
			value3.CreateMiniGameResponseUtc = utcNow;
		}
		bool flag = value3.LaunchCommandsSentUtc != DateTime.MinValue;
		MinigameDiagnosticsLog.Info("TcgPostGoCreateMiniGameRequestObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-direct-start"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("expectedStartScreenStateId", num), MinigameDiagnosticsLog.Field("goConfirmed", true), MinigameDiagnosticsLog.Field("goAgeMs", num2), MinigameDiagnosticsLog.Field("alreadySentFirstGoLaunchBurst", flag), MinigameDiagnosticsLog.Field("sameClickRuntimeContinuation", true), MinigameDiagnosticsLog.Field("suppressedReopenStartScreen", true), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		bool flag2 = !flag;
		if (flag2)
		{
			SendTradingCardStartGameAfterStartScreenGo(connection, worldTunnel, playerGuid, launchTicket, "post-go-create-minigame");
		}
		SendTradingCardGameStartedAfterStartScreenGo(connection, worldTunnel, playerGuid, "post-go-create-minigame", num, -1, -1);
		SendTradingCardGameReady(connection, worldTunnel, playerGuid);
		MinigameDiagnosticsLog.Info("TcgPostGoCreateMiniGameResponseSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "post-go-direct-start"), MinigameDiagnosticsLog.Field("launchCorrelationId", value2), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("requestedGameId", requestedGameId), MinigameDiagnosticsLog.Field("launchCommands", flag2 ? "MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,MiniGameCreateGameResultPacket" : "MiniGame:BeginLoad,MiniGameCreateGameResultPacket"), MinigameDiagnosticsLog.Field("sameClickRuntimeContinuation", true), MinigameDiagnosticsLog.Field("suppressedDuplicateLaunchBurst", flag), MinigameDiagnosticsLog.Field("suppressedDuplicateStartGame", flag), MinigameDiagnosticsLog.Field("suppressedPacket", "MiniGame:JoinGame/subtype16 start-screen opener"), MinigameDiagnosticsLog.Field("didSendBeginLoad", true), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", flag2), MinigameDiagnosticsLog.Field("didGotoPortal", flag2), MinigameDiagnosticsLog.Field("didReopenStartScreen", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		return true;
	}

	private static bool TryHandleTradingCardPostGoAssetsReadyRequest(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int miniGameInstanceId, int requestArgA, int requestArgB, string ticket, ulong launchTicket, int expectedStateId)
	{
		if (string.IsNullOrEmpty(ticket))
		{
			return false;
		}
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, miniGameInstanceId);
		if (!TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey, out var value))
		{
			DateTime utcNow = DateTime.UtcNow;
			value = new TcgStartScreenGoLaunchState
			{
				OpenedUtc = utcNow,
				PreparingStartedUtc = utcNow,
				MiniGameInstanceId = miniGameInstanceId,
				Ticket = (ticket ?? string.Empty),
				LaunchTicket = launchTicket
			};
			TcgStartScreenGoLaunchGuards.TryAdd(tcgStartScreenGoLaunchGuardKey, value);
		}
		DateTime utcNow2 = DateTime.UtcNow;
		DateTime dateTime = ((value.PreparingStartedUtc != DateTime.MinValue) ? value.PreparingStartedUtc : value.OpenedUtc);
		double num = ((dateTime == DateTime.MinValue) ? (-1.0) : Math.Round((utcNow2 - dateTime).TotalMilliseconds, 1));
		bool flag = value.FirstGoUtc != DateTime.MinValue;
		lock (value.Sync)
		{
			if (value.AssetsReadyResponseUtc != DateTime.MinValue)
			{
				double num2 = Math.Round((utcNow2 - value.AssetsReadyResponseUtc).TotalMilliseconds, 1);
				MinigameDiagnosticsLog.Info("TcgPostGoClientAssetsReadyDuplicateSuppressed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("prepAgeMs", num), MinigameDiagnosticsLog.Field("duplicateAgeMs", num2), MinigameDiagnosticsLog.Field("alreadyStarted", true), MinigameDiagnosticsLog.Field("suppressedPackets", "duplicate readiness response; GO is already visible or launch has begun"), MinigameDiagnosticsLog.Field("didLaunch", false));
				return true;
			}
			value.AssetsReadyResponseUtc = utcNow2;
		}
		DateTime openedUtc = DateTime.MinValue;
		int num3 = 41;
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			openedUtc = ((orAdd.OpenedUtc == DateTime.MinValue) ? value.OpenedUtc : orAdd.OpenedUtc);
			num3 = ResolveTradingCardStartScreenDisplayRowId(orAdd.SelectedRowId);
			orAdd.AssetsReadyUtc = utcNow2;
			orAdd.AssetsReadyResponseSent = true;
			orAdd.MiniGameInstanceId = miniGameInstanceId;
			orAdd.Ticket = ticket ?? string.Empty;
			orAdd.LaunchTicket = launchTicket;
			orAdd.State = TcgStartScreenReadinessState.AssetsReady;
		}
		MinigameDiagnosticsLog.Info("TcgClientAssetsReadyObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("expectedStateId", expectedStateId), MinigameDiagnosticsLog.Field("selectedRowId", num3), MinigameDiagnosticsLog.Field("prepAgeMs", num), MinigameDiagnosticsLog.Field("goAlreadyClicked", flag), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.AssetsReady.ToString()), MinigameDiagnosticsLog.Field("didShowGo", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgPostGoClientAssetsReadyRequestObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "client-assets-ready-request"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("expectedStateId", expectedStateId), MinigameDiagnosticsLog.Field("goConfirmed", flag), MinigameDiagnosticsLog.Field("prepAgeMs", num), MinigameDiagnosticsLog.Field("oldDirectPlaySequence", "deferred until first visible GO click"), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgClientAssetsReadyResponseSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("response", "readiness accepted; launch commands deferred until GO"), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgPostGoClientAssetsReadyResponseSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("launchCommands", "deferred until first visible GO click"), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		SendTradingCardStartScreenReadyFromAssetsReady(connection, worldTunnel, playerGuid, reason, openedUtc, utcNow2, ticket, launchTicket, miniGameInstanceId);
		return true;
	}

	private static void SendTradingCardStartScreenReadyFromAssetsReady(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, DateTime openedUtc, DateTime assetsReadyUtc, string ticket, ulong launchTicket, int miniGameInstanceId)
	{
		string value = ((reason == "assets-ready-timeout-fallback") ? "assets-ready-timeout-fallback" : "client-assets-ready-request");
		if (!TryMarkTradingCardStartScreenReadyFromAssetsReady(playerGuid, openedUtc, assetsReadyUtc, out var ageMs, out var selectedRowId))
		{
			MinigameDiagnosticsLog.Info("TcgStartScreenReadyTransitionSkipped", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("skipReason", "start-screen state changed, ready already sent, or GO launch already began"), MinigameDiagnosticsLog.Field("delayMs", 0), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		MinigameDiagnosticsLog.Info("TcgStartScreenReadyTransitionScheduled", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("delayMs", 0), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("scheduledCommand", "MiniGameCreateGameResultPacket/readiness-only + MiniGame:SetMiniGameReady + MinigameStartHandler:setReady + MinigameStartScreen:setSelectedDisplayRowId + MinigameStartScreen:setReady + MinigameStartScreen:setLoading(0)"), MinigameDiagnosticsLog.Field("expectedLuaHandler", "ActivityEvents:OnCreateGameResult -> MinigameStartHandler:setReady"), MinigameDiagnosticsLog.Field("loadingBeforeReady", true), MinigameDiagnosticsLog.Field("actionStateAfterReady", "ready/actionable"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false));
		if (reason == "assets-ready-timeout-fallback")
		{
			MinigameDiagnosticsLog.Info("TcgServerGoStateFallbackSynced", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.GoVisible.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("assetsReady", true), MinigameDiagnosticsLog.Field("goVisible", true), MinigameDiagnosticsLog.Field("launchStarted", false), MinigameDiagnosticsLog.Field("nextSubtype5WillLaunch", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		}
		MinigameDiagnosticsLog.Info("TcgStartScreenReadyNativeCreateGameResultSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("nativeClientEvent", "ActivityEvents:OnCreateGameResult"), MinigameDiagnosticsLog.Field("miniGameId", miniGameInstanceId), MinigameDiagnosticsLog.Field("nativeTcgMiniGameId", 387), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("readinessOnly", true), MinigameDiagnosticsLog.Field("expectedActionState", "ready/actionable"), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		SendTradingCardGameReady(connection, worldTunnel, playerGuid, reason + "-start-screen-ready", readinessOnly: true, miniGameInstanceId);
		SendMiniGameSetReadyRegisteredEvent(connection, worldTunnel, playerGuid, reason + "-assets-ready");
		SendMiniGameStartHandlerCommand(connection, worldTunnel, playerGuid, reason + "-assets-ready-visual-handler-fallback", "setReady");
		SendMiniGameStartScreenSelectedDisplayRowCommand(connection, worldTunnel, playerGuid, reason + "-assets-ready-selected-display-row", selectedRowId);
		SendMiniGameStartScreenCommand(connection, worldTunnel, playerGuid, reason + "-assets-ready-visual-screen-fallback", "setReady");
		SendMiniGameStartScreenIntCommand(connection, worldTunnel, playerGuid, reason + "-assets-ready-visual-screen-set-loading-uppercase", "SetLoading", default(int));
		SendMiniGameStartScreenIntCommand(connection, worldTunnel, playerGuid, reason + "-assets-ready-visual-screen-set-loading-lowercase", "setLoading", default(int));
		MinigameDiagnosticsLog.Info("TcgStartScreenReadyVisualFallbackSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("commands", "MiniGameCreateGameResultPacket/readiness-only,MiniGame:SetMiniGameReady,MinigameStartHandler:setReady,MinigameStartScreen:setSelectedDisplayRowId,MinigameStartScreen:setReady,MinigameStartScreen:SetLoading(0),MinigameStartScreen:setLoading(0)"), MinigameDiagnosticsLog.Field("expectedSwfEffect", "ActivityEvents:OnCreateGameResult and setLoading(false) hide spinner and enable action"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgStartScreenReadyVisibleExpected", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("command", "setReady"), MinigameDiagnosticsLog.Field("script", "MiniGame:SetMiniGameReady"), MinigameDiagnosticsLog.Field("expectedLuaHandler", "MinigameStartHandler:setReady"), MinigameDiagnosticsLog.Field("delayMs", 0), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("ageMs", ageMs), MinigameDiagnosticsLog.Field("expectedActionState", "ready/actionable"), MinigameDiagnosticsLog.Field("expectedSpinnerVisible", false), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false));
		MinigameDiagnosticsLog.Info("TcgGoVisible", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.GoVisible.ToString()), MinigameDiagnosticsLog.Field("actionState", "ready/actionable"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("ageMs", ageMs), MinigameDiagnosticsLog.Field("readinessGate", value), MinigameDiagnosticsLog.Field("nextClickWillLaunch", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
	}

	private static bool TryMarkTradingCardStartScreenReadyFromAssetsReady(ulong playerGuid, DateTime openedUtc, DateTime assetsReadyUtc, out double ageMs, out int selectedRowId)
	{
		DateTime utcNow = DateTime.UtcNow;
		ageMs = Math.Round((utcNow - ((openedUtc == DateTime.MinValue) ? assetsReadyUtc : openedUtc)).TotalMilliseconds, 1);
		selectedRowId = 41;
		lock (TcgStartScreenFlowSync)
		{
			if (!TcgStartScreenFlows.TryGetValue(playerGuid, out var value) || value.GoConfirmed || value.LaunchStarted || value.ReadySent)
			{
				return false;
			}
			if (openedUtc != DateTime.MinValue && value.OpenedUtc != DateTime.MinValue && value.OpenedUtc != openedUtc)
			{
				return false;
			}
			value.AssetsReadyUtc = assetsReadyUtc;
			value.ReadySent = true;
			value.ReadyUtc = utcNow;
			value.GoVisibleUtc = utcNow;
			value.State = TcgStartScreenReadinessState.GoVisible;
			selectedRowId = ResolveTradingCardStartScreenDisplayRowId(value.SelectedRowId);
			return true;
		}
	}

	private static bool TryHandleTradingCardStartScreenGoRequest(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int miniGameInstanceId, int requestArgA, int requestArgB, string ticket, ulong launchTicket, int expectedStateId, int expectedGroupId, int expectedGameId)
	{
		double num = -1.0;
		int selectedRowId;
		int preReadyRequestStartGameCount;
		bool readySent;
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			readySent = orAdd.ReadySent;
			selectedRowId = ResolveTradingCardStartScreenDisplayRowId(orAdd.SelectedRowId);
			if (!readySent)
			{
				orAdd.PreReadyRequestStartGameCount++;
				preReadyRequestStartGameCount = orAdd.PreReadyRequestStartGameCount;
			}
			else
			{
				preReadyRequestStartGameCount = orAdd.PreReadyRequestStartGameCount;
				num = Math.Round((DateTime.UtcNow - orAdd.ReadyUtc).TotalMilliseconds, 1);
			}
		}
		var (num2, value, value2) = ResolveTradingCardStartScreenDisplayForDiagnostics(selectedRowId);
		if (!readySent)
		{
			DateTime utcNow = DateTime.UtcNow;
			lock (TcgStartScreenFlowSync)
			{
				TcgStartScreenFlowState orAdd2 = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
				orAdd2.ReadySent = true;
				orAdd2.ReadyUtc = utcNow;
				orAdd2.GoVisibleUtc = utcNow;
				orAdd2.State = TcgStartScreenReadinessState.GoVisible;
			}
			readySent = true;
			num = 0.0;
			MinigameDiagnosticsLog.Info("TcgStartScreenPreReadyRequestStartGameAccepted", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("expectedStateId", expectedStateId), MinigameDiagnosticsLog.Field("expectedGroupId", expectedGroupId), MinigameDiagnosticsLog.Field("expectedGameId", expectedGameId), MinigameDiagnosticsLog.Field("preReadyCount", preReadyRequestStartGameCount), MinigameDiagnosticsLog.Field("readySent", false), MinigameDiagnosticsLog.Field("response", "mark start screen ready only; launch requires a later post-ready GO request"), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
			SendMiniGameStartScreenSelectedDisplayRowCommand(connection, worldTunnel, playerGuid, reason + "-pre-ready-subtype5-selected-display-row", num2);
			SendMiniGameStartScreenCommand(connection, worldTunnel, playerGuid, reason + "-pre-ready-subtype5-set-ready", "setReady");
			return true;
		}
		DateTime utcNow2 = DateTime.UtcNow;
		string tcgStartScreenGoLaunchGuardKey = GetTcgStartScreenGoLaunchGuardKey(playerGuid, ticket, launchTicket, miniGameInstanceId);
		string value3 = BuildTcgLaunchCorrelationId(playerGuid, ticket, launchTicket);
		TcgStartScreenGoLaunchState tcgStartScreenGoLaunchState;
		if (TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey, out var value4))
		{
			lock (value4.Sync)
			{
				if (value4.LaunchStartedUtc != DateTime.MinValue)
				{
					double num3 = Math.Round((utcNow2 - value4.LaunchStartedUtc).TotalMilliseconds, 1);
					MinigameDiagnosticsLog.Info("TcgStartScreenGoDuplicateSuppressed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", BuildTcgLaunchCorrelationId(playerGuid, value4.Ticket, value4.LaunchTicket)), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("ticket", value4.Ticket), MinigameDiagnosticsLog.Field("launchTicket", value4.LaunchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", value4.MiniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedTitle", value), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("readyAgeMs", num), MinigameDiagnosticsLog.Field("duplicateAgeMs", num3), MinigameDiagnosticsLog.Field("alreadyStarted", true), MinigameDiagnosticsLog.Field("suppressedCall", "post-ready GO launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
					return true;
				}
				value4.FirstGoUtc = utcNow2;
				value4.LaunchStartedUtc = utcNow2;
				value4.MiniGameInstanceId = miniGameInstanceId;
				value4.Ticket = ticket ?? string.Empty;
				value4.LaunchTicket = launchTicket;
				tcgStartScreenGoLaunchState = value4;
			}
		}
		else
		{
			tcgStartScreenGoLaunchState = new TcgStartScreenGoLaunchState
			{
				FirstGoUtc = utcNow2,
				LaunchStartedUtc = utcNow2,
				MiniGameInstanceId = miniGameInstanceId,
				Ticket = (ticket ?? string.Empty),
				LaunchTicket = launchTicket
			};
			if (!TcgStartScreenGoLaunchGuards.TryAdd(tcgStartScreenGoLaunchGuardKey, tcgStartScreenGoLaunchState) && TcgStartScreenGoLaunchGuards.TryGetValue(tcgStartScreenGoLaunchGuardKey, out value4))
			{
				lock (value4.Sync)
				{
					if (value4.LaunchStartedUtc != DateTime.MinValue)
					{
						double num4 = Math.Round((utcNow2 - value4.LaunchStartedUtc).TotalMilliseconds, 1);
						MinigameDiagnosticsLog.Info("TcgStartScreenGoDuplicateSuppressed", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", BuildTcgLaunchCorrelationId(playerGuid, value4.Ticket, value4.LaunchTicket)), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("ticket", value4.Ticket), MinigameDiagnosticsLog.Field("launchTicket", value4.LaunchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", value4.MiniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedTitle", value), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("readyAgeMs", num), MinigameDiagnosticsLog.Field("duplicateAgeMs", num4), MinigameDiagnosticsLog.Field("alreadyStarted", true), MinigameDiagnosticsLog.Field("suppressedCall", "post-ready GO launch"), MinigameDiagnosticsLog.Field("didLaunch", false));
						return true;
					}
					value4.FirstGoUtc = utcNow2;
					value4.LaunchStartedUtc = utcNow2;
					value4.MiniGameInstanceId = miniGameInstanceId;
					value4.Ticket = ticket ?? string.Empty;
					value4.LaunchTicket = launchTicket;
					tcgStartScreenGoLaunchState = value4;
				}
			}
		}
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd3 = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			orAdd3.GoConfirmed = true;
			orAdd3.GoClickedUtc = utcNow2;
			orAdd3.LaunchInProgressUtc = utcNow2;
			orAdd3.LaunchStarted = true;
			orAdd3.State = TcgStartScreenReadinessState.LaunchInProgress;
		}
		MinigameDiagnosticsLog.Info("TcgStartScreenGoClickedRequestObserved", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("clientRequestType", "RequestStartGame/subtype5"), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedTitle", value), MinigameDiagnosticsLog.Field("selectedDescription", value2), MinigameDiagnosticsLog.Field("requestArgA", requestArgA), MinigameDiagnosticsLog.Field("requestArgB", requestArgB), MinigameDiagnosticsLog.Field("expectedStateId", expectedStateId), MinigameDiagnosticsLog.Field("expectedGroupId", expectedGroupId), MinigameDiagnosticsLog.Field("expectedGameId", expectedGameId), MinigameDiagnosticsLog.Field("readyAgeMs", num), MinigameDiagnosticsLog.Field("preReadySubtype5Suppressed", preReadyRequestStartGameCount), MinigameDiagnosticsLog.Field("buttonState", "GO"), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgStartScreenGoStartAction", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("ticket", ticket), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("selectedTitle", value), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("restoredNativeTcgRow", 727), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("nextFlow", "ClientActivityLaunch:CompleteTcgLaunchAfterStartScreenGo + immediate accepted launch"), MinigameDiagnosticsLog.Field("didSendBeginLoadBeforeGo", false), MinigameDiagnosticsLog.Field("didShowHudBeforeGo", false), MinigameDiagnosticsLog.Field("didLaunchTcgDllBeforeGo", false));
		MinigameDiagnosticsLog.Info("TcgGoLaunchStarted", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.LaunchInProgress.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("nativeMiniGameId", 387), MinigameDiagnosticsLog.Field("activityId", 7), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("readinessAlreadyConfirmed", true), MinigameDiagnosticsLog.Field("duplicateGuardState", "launch started; later subtype5 suppressed"));
		MinigameDiagnosticsLog.Info("TcgGoContinuationInlinedOnFirstClick", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("inlineContinuation", true), MinigameDiagnosticsLog.Field("commands", "CompleteTcgLaunchAfterStartScreenGo,SendAcceptedTcgLaunchAfterStartScreenGo,MiniGamePlayerStartMiniGame,MiniGameBeginLoad,MiniGameCreateGameResultPacket,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"));
		MinigameDiagnosticsLog.Info("TcgFirstGoCompletesLaunch", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("TcgFirstGoCompletesLaunch", true), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("duplicateGuardState", "first GO owns launch; duplicates suppressed after LaunchStarted=true"));
		SendTradingCardGameStartedAfterStartScreenGo(connection, worldTunnel, playerGuid, "sanctuary-subtype5-echo-before-tcg-launch", miniGameInstanceId, requestArgA, requestArgB);
		ClientActivityLaunchPacketHandler.CompleteTcgLaunchAfterStartScreenGo(connection, worldTunnel, playerGuid, ticket, num2);
		bool flag = ClientActivityLaunchPacketHandler.SendAcceptedTcgLaunchAfterStartScreenGo(connection, worldTunnel, playerGuid, ticket, "post-ready-go-click-immediate");
		lock (tcgStartScreenGoLaunchState.Sync)
		{
			tcgStartScreenGoLaunchState.LaunchCommandsSentUtc = DateTime.UtcNow;
		}
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlowState orAdd4 = TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState());
			orAdd4.LaunchCommandsSentUtc = DateTime.UtcNow;
			orAdd4.State = TcgStartScreenReadinessState.LaunchCommandsSent;
		}
		MinigameDiagnosticsLog.Info("TcgGoLaunchCommandsSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.LaunchCommandsSent.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("selectedRowId", num2), MinigameDiagnosticsLog.Field("launchCommands", "ClientActivityLaunch:CompleteTcgLaunchAfterStartScreenGo,tcg-accepted-launch-sent,MiniGame:PlayerStartMiniGame,MiniGame:BeginLoad,MiniGameCreateGameResultPacket,TradingCardGameHandler:initiateGame,TradingCardGameHandler:gotoPortal"), MinigameDiagnosticsLog.Field("didSendAcceptedLaunchImmediately", flag), MinigameDiagnosticsLog.Field("didSendBeginLoad", flag), MinigameDiagnosticsLog.Field("didShowHud", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", flag), MinigameDiagnosticsLog.Field("didGotoPortal", true), MinigameDiagnosticsLog.Field("didLaunchBeforeGo", false), MinigameDiagnosticsLog.Field("awaiting", "FreeRealmsTCG.dll load or TCG server connection"));
		lock (TcgStartScreenFlowSync)
		{
			TcgStartScreenFlows.GetOrAdd(playerGuid, (ulong _) => new TcgStartScreenFlowState()).State = TcgStartScreenReadinessState.AwaitingTcgDllLoad;
		}
		MinigameDiagnosticsLog.Info("TcgGoLaunchAwaitingRuntime", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("launchCorrelationId", value3), MinigameDiagnosticsLog.Field("state", TcgStartScreenReadinessState.AwaitingTcgDllLoad.ToString()), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("launchTicket", launchTicket), MinigameDiagnosticsLog.Field("miniGameInstanceId", miniGameInstanceId), MinigameDiagnosticsLog.Field("launchComplete", false), MinigameDiagnosticsLog.Field("duplicateGuardState", "LaunchStarted remains true; repeated GO/subtype5 is suppressed"));
		return true;
	}

	private static void SendMiniGameStartHandlerCommand(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string methodName)
	{
		string text = "MinigameStartHandler:" + methodName;
		bool flag = IsKnownRegisteredMiniGameUiEvent(text);
		bool flag2 = IsNativeMiniGameStartHandlerMethod(text);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(text);
		packetWriter.Write(Array.Empty<int>());
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		bool worldTunnel2 = worldTunnel;
		string text2 = MinigameDiagnosticsLog.Field("reason", reason);
		string text3 = MinigameDiagnosticsLog.Field("script", text);
		string text4 = MinigameDiagnosticsLog.Field("method", methodName);
		string text5 = MinigameDiagnosticsLog.Field("command", methodName);
		string text6 = MinigameDiagnosticsLog.Field("targetHandler", "MinigameStartHandler");
		string text7 = MinigameDiagnosticsLog.Field("opcode47DirectRegistered", flag);
		string text8 = MinigameDiagnosticsLog.Field("nativeHandlerMethodExists", flag2);
		string text9 = MinigameDiagnosticsLog.Field("registrationEvidence", GetMiniGameUiCommandRegistrationEvidence(text));
		string value = methodName switch
		{
			"setNotReady" => "MiniGame:SetMiniGameReady(false)/MiniGameStartHandler:setNotReady", 
			"show" => "MiniGameStartHandler:show -> MinigameStartScreen:Show", 
			"setReady" => "MiniGame:SetMiniGameReady(true)/MiniGameStartHandler:setReady", 
			_ => "MiniGameStartHandler:" + methodName, 
		};
		MinigameDiagnosticsLog.Info("HandlerMiniGameStartCommandSent", "MiniGamePacketHandler", playerGuid, worldTunnel2, text2, text3, text4, text5, text6, text7, text8, text9, MinigameDiagnosticsLog.Field("nativeClientCommand", value), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("paramCount", 0), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendMiniGameSetReadyRegisteredEvent(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write("MiniGame:SetMiniGameReady");
		packetWriter.Write(Array.Empty<int>());
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("NativeMiniGameSetReadyEventSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", "MiniGame:SetMiniGameReady"), MinigameDiagnosticsLog.Field("command", "setReady"), MinigameDiagnosticsLog.Field("registeredEvent", IsKnownRegisteredMiniGameUiEvent("MiniGame:SetMiniGameReady")), MinigameDiagnosticsLog.Field("expectedNativeEffect", "sub_9B0CC0 marks the current minigame state ready and dispatches HandlerMiniGameStart:setReady"), MinigameDiagnosticsLog.Field("expectedLuaHandler", "MinigameStartHandler:setReady -> MinigameStartScreen:SetLoading(false)"), MinigameDiagnosticsLog.Field("rawDirectLuaCallSent", false), MinigameDiagnosticsLog.Field("didSendBeginLoad", false), MinigameDiagnosticsLog.Field("didShowHudMinigame", false), MinigameDiagnosticsLog.Field("didGotoPortal", false), MinigameDiagnosticsLog.Field("didInitiateGame", false), MinigameDiagnosticsLog.Field("didLaunchTcgDll", false), MinigameDiagnosticsLog.Field("paramCount", 0), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendMiniGameStartScreenIntCommand(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string methodName, params int[] args)
	{
		string text = "MinigameStartScreen:" + methodName;
		bool flag = IsKnownRegisteredMiniGameUiEvent(text);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(text);
		packetWriter.Write(args ?? Array.Empty<int>());
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("MinigameStartScreenDirectCommandSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", text), MinigameDiagnosticsLog.Field("method", methodName), MinigameDiagnosticsLog.Field("targetHandler", "MinigameStartScreen"), MinigameDiagnosticsLog.Field("opcode47DirectRegistered", flag), MinigameDiagnosticsLog.Field("nativeHandlerMethodExists", false), MinigameDiagnosticsLog.Field("registrationEvidence", GetMiniGameUiCommandRegistrationEvidence(text)), MinigameDiagnosticsLog.Field("nativeClientCommand", text + "(" + string.Join(",", args ?? Array.Empty<int>()) + ")"), MinigameDiagnosticsLog.Field("diagnosticGoal", "Direct MinigameStartScreen table calls are logged as unregistered unless native evidence is found; the current start-screen-only path does not use them."), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("paramCount", (args != null) ? args.Length : 0), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendMiniGameStartScreenCommand(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string methodName)
	{
		string text = "MinigameStartScreen:" + methodName;
		bool flag = IsKnownRegisteredMiniGameUiEvent(text);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(text);
		packetWriter.Write(Array.Empty<int>());
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("MinigameStartScreenDirectCommandSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", text), MinigameDiagnosticsLog.Field("method", methodName), MinigameDiagnosticsLog.Field("targetHandler", "MinigameStartScreen"), MinigameDiagnosticsLog.Field("opcode47DirectRegistered", flag), MinigameDiagnosticsLog.Field("nativeHandlerMethodExists", false), MinigameDiagnosticsLog.Field("registrationEvidence", GetMiniGameUiCommandRegistrationEvidence(text)), MinigameDiagnosticsLog.Field("nativeClientCommand", text + "()"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("paramCount", 0), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void SendMiniGameStartScreenSelectedDisplayRowCommand(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int selectedDisplayRowId)
	{
		SendMiniGameStartScreenIntCommand(connection, worldTunnel, playerGuid, reason, "setSelectedDisplayRowId", selectedDisplayRowId, selectedDisplayRowId);
	}

	private static bool IsKnownRegisteredMiniGameUiEvent(string script)
	{
		switch (script)
		{
		default:
			return script == "MiniGameStartHandler:showStageSelect";
		case "ActivityEvents:OnCreateGameResult":
		case "GameEvents:OnPlayerStartMiniGame":
		case "MiniGame:PlayerStartMiniGame":
		case "MiniGame:SetMiniGameReady":
		case "MiniGame:ShowStageSelect":
		case "MiniGame:StartHandler:Show":
			return true;
		}
	}

	private static bool IsNativeMiniGameStartHandlerMethod(string script)
	{
		switch (script)
		{
		default:
			return script == "MinigameStartHandler:exit";
		case "MinigameStartHandler:show":
		case "MinigameStartHandler:setNotReady":
		case "MinigameStartHandler:setReady":
			return true;
		}
	}

	private static string GetMiniGameUiCommandRegistrationEvidence(string script)
	{
		if (IsKnownRegisteredMiniGameUiEvent(script))
		{
			return "native string found in FreeRealms.exe decompile; this should be raised by the matching native packet/event path, not arbitrary opcode47 table dispatch";
		}
		if (IsNativeMiniGameStartHandlerMethod(script))
		{
			return "Lua handler method exists, but direct opcode47 dispatch is unproven and was ignored by the start-screen tests";
		}
		return "no native registration evidence found; treat as arbitrary Lua/table call";
	}

	private static void WriteTradingCardMiniGameGroupInfo(PacketWriter packetWriter, ulong playerGuid, bool worldTunnel, string reason, TradingCardGroupElementRow[] visibleElements)
	{
		packetWriter.Write(36);
		packetWriter.Write(3388);
		packetWriter.Write(401571);
		packetWriter.Write(7533);
		packetWriter.Write(string.Empty);
		packetWriter.Write(41);
		packetWriter.Write(visibleElements.Length);
		foreach (TradingCardGroupElementRow row in visibleElements)
		{
			WriteTradingCardMiniGameGroupElement(packetWriter, row, playerGuid, worldTunnel, reason);
		}
		packetWriter.Write(string.Empty);
		packetWriter.Write((byte)0);
		packetWriter.Write(0);
	}

	private static void WriteTradingCardMiniGameGroupElement(PacketWriter packetWriter, TradingCardGroupElementRow row, ulong playerGuid, bool worldTunnel, string reason)
	{
		TcgDetailUiPatch.LocaleLookupResult localeLookupResult = TcgDetailUiPatch.ResolveUiGetStringByIdForDiagnostics(row.NameId);
		TcgDetailUiPatch.LocaleLookupResult localeLookupResult2 = TcgDetailUiPatch.ResolveUiGetStringByIdForDiagnostics(row.DescriptionId);
		packetWriter.Write(row.LinkId);
		packetWriter.Write(row.TargetMiniGameDataId);
		packetWriter.Write(row.DetailImage);
		packetWriter.Write(row.ThumbnailImage);
		packetWriter.Write((byte)1);
		packetWriter.Write(1);
		packetWriter.Write(row.NameId);
		packetWriter.Write(row.DescriptionId);
		packetWriter.Write(row.IconId);
		packetWriter.Write(row.TargetMiniGameDataId);
		packetWriter.Write((byte)0);
		packetWriter.Write(0);
		packetWriter.Write("Levels 1 - 5");
		packetWriter.Write(row.Difficulty);
		packetWriter.Write(row.Position);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write((byte)1);
		MinigameDiagnosticsLog.Info("TcgMiniGameGroupElementWireRow", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("wireOrder", "linkId,targetMiniGameDataId,detailImage,thumbnailImage,unlockedFlag,quantity,nameToken,descriptionToken,iconId,parentMiniGameDataId,membersOnlyFlag,mysteryChestIcon,levelString,difficulty,position,stageNumber,mustPurchaseItemId,priceItemId,enabledFlag"), MinigameDiagnosticsLog.Field("debugName", row.DebugName), MinigameDiagnosticsLog.Field("linkIdSigned", row.LinkId), MinigameDiagnosticsLog.Field("linkIdUnsigned", Unsigned(row.LinkId)), MinigameDiagnosticsLog.Field("targetMiniGameDataIdSigned", row.TargetMiniGameDataId), MinigameDiagnosticsLog.Field("unlockedFlag", 1), MinigameDiagnosticsLog.Field("quantity", 1), MinigameDiagnosticsLog.Field("nameSlot", "element[0xb]"), MinigameDiagnosticsLog.Field("nameIdSigned", row.NameId), MinigameDiagnosticsLog.Field("nameIdUnsigned", Unsigned(row.NameId)), MinigameDiagnosticsLog.Field("nameIdHex", Hex(row.NameId)), MinigameDiagnosticsLog.Field("nameResolved", localeLookupResult.Resolved), MinigameDiagnosticsLog.Field("nameResolvedLookupId", localeLookupResult.LookupId), MinigameDiagnosticsLog.Field("nameResolvedString", localeLookupResult.Text), MinigameDiagnosticsLog.Field("nameResolvedSource", localeLookupResult.Source), MinigameDiagnosticsLog.Field("nameCodeStringMappingExists", localeLookupResult.CodeStringMappingExists), MinigameDiagnosticsLog.Field("nameCodeStringMappingKey", localeLookupResult.CodeStringMappingKey), MinigameDiagnosticsLog.Field("nameUiGetStringByIdAllowed", localeLookupResult.UiGetStringByIdAllowed), MinigameDiagnosticsLog.Field("descriptionSlot", "element[0xd]"), MinigameDiagnosticsLog.Field("descriptionIdSigned", row.DescriptionId), MinigameDiagnosticsLog.Field("descriptionIdUnsigned", Unsigned(row.DescriptionId)), MinigameDiagnosticsLog.Field("descriptionIdHex", Hex(row.DescriptionId)), MinigameDiagnosticsLog.Field("descriptionResolved", localeLookupResult2.Resolved), MinigameDiagnosticsLog.Field("descriptionResolvedLookupId", localeLookupResult2.LookupId), MinigameDiagnosticsLog.Field("descriptionResolvedString", localeLookupResult2.Text), MinigameDiagnosticsLog.Field("descriptionResolvedSource", localeLookupResult2.Source), MinigameDiagnosticsLog.Field("descriptionCodeStringMappingExists", localeLookupResult2.CodeStringMappingExists), MinigameDiagnosticsLog.Field("descriptionCodeStringMappingKey", localeLookupResult2.CodeStringMappingKey), MinigameDiagnosticsLog.Field("descriptionUiGetStringByIdAllowed", localeLookupResult2.UiGetStringByIdAllowed), MinigameDiagnosticsLog.Field("finalNameExpectedByNative", localeLookupResult.Text), MinigameDiagnosticsLog.Field("finalDescriptionExpectedByNative", localeLookupResult2.Text), MinigameDiagnosticsLog.Field("finalNameExpectedByUiGetStringById", localeLookupResult.Text), MinigameDiagnosticsLog.Field("finalDescriptionExpectedByUiGetStringById", localeLookupResult2.Text), MinigameDiagnosticsLog.Field("iconSlot", "element[0xe]"), MinigameDiagnosticsLog.Field("iconId", row.IconId), MinigameDiagnosticsLog.Field("parentMiniGameDataId", row.TargetMiniGameDataId), MinigameDiagnosticsLog.Field("membersOnlyFlag", 0), MinigameDiagnosticsLog.Field("mysteryChestIcon", 0), MinigameDiagnosticsLog.Field("levelString", "Levels 1 - 5"), MinigameDiagnosticsLog.Field("position", row.Position), MinigameDiagnosticsLog.Field("difficulty", row.Difficulty), MinigameDiagnosticsLog.Field("stageNumber", 0), MinigameDiagnosticsLog.Field("mustPurchaseItemId", 0), MinigameDiagnosticsLog.Field("priceItemId", 0), MinigameDiagnosticsLog.Field("enabledFlag", 1), MinigameDiagnosticsLog.Field("detailImage", row.DetailImage), MinigameDiagnosticsLog.Field("detailImagePublicAssetUrl", TcgDetailUiPatch.BuildPublicAssetUrlForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("detailImageExists", TcgDetailUiPatch.ClientResourceAssetExistsForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("detailImageLooseResource", TcgDetailUiPatch.ResolveClientResourceAssetForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("thumbnailImage", row.ThumbnailImage), MinigameDiagnosticsLog.Field("thumbnailImagePublicAssetUrl", TcgDetailUiPatch.BuildPublicAssetUrlForDiagnostics(row.ThumbnailImage)), MinigameDiagnosticsLog.Field("thumbnailImageExists", TcgDetailUiPatch.ClientResourceAssetExistsForDiagnostics(row.ThumbnailImage)), MinigameDiagnosticsLog.Field("thumbnailImageLooseResource", TcgDetailUiPatch.ResolveClientResourceAssetForDiagnostics(row.ThumbnailImage)));
	}

	private static TradingCardGroupElementRow[] GetVisibleTradingCardGroupElements(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		if (!TcgDetailUiPatch.ShouldIncludePoeTournamentDeck(connection, worldTunnel, playerGuid, reason, "minigame-group-info"))
		{
			return TradingCardDefaultGroupElements;
		}
		return new List<TradingCardGroupElementRow>(TradingCardDefaultGroupElements) { PoeTournamentGroupElement }.ToArray();
	}

	private static string BuildTradingCardGroupElementLinkIdList(TradingCardGroupElementRow[] rows)
	{
		return string.Join(",", Array.ConvertAll(rows, (TradingCardGroupElementRow row) => row.LinkId.ToString()));
	}

	private static string BuildTradingCardGroupElementTargetIdList(TradingCardGroupElementRow[] rows)
	{
		return string.Join(",", Array.ConvertAll(rows, (TradingCardGroupElementRow row) => row.TargetMiniGameDataId.ToString()));
	}

	private static string BuildTradingCardGroupElementNameList(TradingCardGroupElementRow[] rows)
	{
		return string.Join(",", Array.ConvertAll(rows, (TradingCardGroupElementRow row) => row.DebugName));
	}

	private static string GetTradingCardRowTitle(int rowId)
	{
		return rowId switch
		{
			41 => "Free Realms Trading Card Game Lobby", 
			603 => "Bry's Tournament Deck", 
			604 => "Bry's Trick Deck", 
			602 => "Free Realms Trading Card Game Tutorial", 
			499 => "Poe's Tournament Deck", 
			507 => "Poe's Tournament Deck", 
			387 => "Free Realms Trading Card Game Lobby", 
			_ => string.Empty, 
		};
	}

	private static uint Unsigned(int value)
	{
		return (uint)value;
	}

	private static string Hex(int value)
	{
		return "0x" + Unsigned(value).ToString("X8");
	}

	private static bool AckSubpacket(GatewayConnection connection, byte responseType, bool worldTunnel)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write(responseType);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("ack-sent", "MiniGamePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("responseType", responseType), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool EchoSubpacket(GatewayConnection connection, ReadOnlySpan<byte> rawPayload, bool worldTunnel)
	{
		MatchmakingPacketHandler.Send(connection, worldTunnel, rawPayload.ToArray());
		MinigameDiagnosticsLog.Info("echo-sent", "MiniGamePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.HexField("payload", rawPayload));
		return true;
	}

	private static bool SendMiniGameLeave(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int stateId, byte requestType, string requestName)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)19);
		packetWriter.Write(stateId);
		packetWriter.Write(-1);
		packetWriter.Write(-1);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("MiniGameLeavePacketSent", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sourceRequestType", requestType), MinigameDiagnosticsLog.Field("sourceRequestName", requestName), MinigameDiagnosticsLog.Field("stateId", stateId), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool LogUnhandled(byte packetType, ulong playerGuid, bool worldTunnel, ReadOnlySpan<byte> remaining)
	{
		_logger.LogInformation("Unhandled minigame packet type={type}, Player={player}", packetType, playerGuid);
		MinigameDiagnosticsLog.Warn("unhandled-packet", "MiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", "MiniGame"), MinigameDiagnosticsLog.Field("type", packetType), MinigameDiagnosticsLog.HexField("remaining", remaining));
		return true;
	}
}
