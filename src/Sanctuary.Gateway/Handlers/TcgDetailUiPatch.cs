using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Sanctuary.Core.IO;

namespace Sanctuary.Gateway.Handlers;

internal static class TcgDetailUiPatch
{
	internal readonly struct LocaleLookupResult
	{
		public readonly bool Resolved;

		public readonly string Text;

		public readonly string Source;

		public readonly uint LookupId;

		public readonly bool CodeStringMappingExists;

		public readonly string CodeStringMappingKey;

		public readonly int UiGetStringByIdToken;

		public readonly bool UiGetStringByIdAllowed;

		public LocaleLookupResult(bool resolved, string text, string source, uint lookupId, bool codeStringMappingExists = false, string codeStringMappingKey = "", int uiGetStringByIdToken = 0, bool uiGetStringByIdAllowed = true)
		{
			Resolved = resolved;
			Text = text;
			Source = source;
			LookupId = lookupId;
			CodeStringMappingExists = codeStringMappingExists;
			CodeStringMappingKey = codeStringMappingKey;
			UiGetStringByIdToken = uiGetStringByIdToken;
			UiGetStringByIdAllowed = uiGetStringByIdAllowed;
		}
	}

	private readonly struct ActivityRow
	{
		public readonly int Id;

		public readonly int AppSystemId;

		public readonly int MiniGameDataId;

		public readonly int DisplayNameId;

		public readonly int DisplayDescriptionId;

		public readonly int IconId;

		public readonly int Position;

		public readonly int Difficulty;

		public readonly int ServerType;

		public readonly string DetailImage;

		public readonly string ThumbnailImage;

		public readonly string DebugName;

		public ActivityRow(int id, int appSystemId, int miniGameDataId, int displayNameId, int displayDescriptionId, int iconId, int position, int difficulty, int serverType, string detailImage, string thumbnailImage, string debugName)
		{
			Id = id;
			AppSystemId = appSystemId;
			MiniGameDataId = miniGameDataId;
			DisplayNameId = displayNameId;
			DisplayDescriptionId = displayDescriptionId;
			IconId = iconId;
			Position = position;
			Difficulty = difficulty;
			ServerType = serverType;
			DetailImage = detailImage;
			ThumbnailImage = thumbnailImage;
			DebugName = debugName;
		}
	}

	private readonly struct MiniGameDataDiagnosticRow
	{
		public readonly int RowId;

		public readonly int TypeId;

		public readonly int NameId;

		public readonly int DescriptionId;

		public readonly int ImageId;

		public readonly string RawLine;

		public MiniGameDataDiagnosticRow(int rowId, int typeId, int nameId, int descriptionId, int imageId, string rawLine)
		{
			RowId = rowId;
			TypeId = typeId;
			NameId = nameId;
			DescriptionId = descriptionId;
			ImageId = imageId;
			RawLine = rawLine;
		}
	}

	private const int TcgActivityCategoryId = 11;

	public const int TreasureWarActivityCategoryAliasId = 26;

	private const int TcgDetailGroupId = 36;

	public const int ActivityId = 24;

	public const int NativeMiniGameTypeId = 23;

	public const int MiniGameId = 727;

	public const int NameId = 3388;

	public const int DescriptionId = 401571;

	public const int ImageSetId = 7589;

	public const string DetailImageFileName = "tcg_lobby_detail.dds";

	public const string ThumbnailImageFileName = "icon_category_minigames_tradingcardgame_128.dds";

	public const string NativeLuaClass = "TradingCardMain";

	public const string PackageName = "game_tcg";

	private const int ActivityListUpdateMode = 1;

	private const int SelectGamePopulateDelayMs = 200;

	private const int FirstSelectPopulateRetryDelayMs = 200;

	private const string PacketReadySuppressedRows = "41,603,604,602";

	private const string ActivityRowWireOrder = "id,appSystemId,category,imageSetId,activityPositionId,displayNameId,displayDescriptionId,nameId,descriptionId,serverType,canPlayerJoinByte,preferredRequirementId,featuredEntryCount,tutorialActivityId,membersOnly,detailImageFilename,thumbnailImageFilename,difficulty,unknown3BackingMiniGameDataId,mysteryChestId,mysteryChestIcon";

	private const string PreviousMisalignedActivityRowWireOrder = "id,appSystemId,category,displayNameId,displayDescriptionId,nameId,descriptionId,imageSetId,activityPositionId,canPlayerJoinInt,isFeaturedByte,preferredRequirementId,featuredEntryCount,tutorialActivityId,membersOnly,detailImageFilename,thumbnailImageFilename,difficulty,featuredRewardTooltipStringId,mysteryChestIcon,chestId";

	private const string ActivityNativeParserEvidence = "FreeRealms.exe FUN_00be63e0 parses raw ClientActivityDefinition slots, but MinigameDetail.lua reads Activities.Category through FUN_00cfc180. The datasource getter maps NameId to native +0x28 and DescriptionId to +0x2c, so the packet must place text tokens in those getter slots.";

	private const string TcgLiteralOverrideResetMethod = "MinigameDetail:items_mc.resetItems";

	private static readonly bool EnableTcgLiteralOverride = true;

	private static readonly object LocaleLock = new object();

	private static readonly object CodeStringMappingLock = new object();

	private static readonly ConcurrentDictionary<ulong, byte> FirstSelectPopulateRetryScheduledPlayers = new ConcurrentDictionary<ulong, byte>();

	private static Dictionary<uint, string> _localeStrings;

	private static string _localeSource = "not-loaded";

	private static Dictionary<int, string> _codeStringMappingsById;

	private static string _codeStringMappingSource = "not-loaded";

	private static readonly Dictionary<int, uint> KnownCodeStringLocaleLookupIdsById = new Dictionary<int, uint>
	{
		{ 3388, 1594357759u },
		{ 434188, 4035014569u },
		{ 401571, 2386199548u },
		{ 433337, 903298991u }
	};

	private static readonly Dictionary<int, string> RuntimeTcgStringOverridesById = new Dictionary<int, string>
	{
		{ 451041, "Main Lobby" },
		{ 451611, "Enter the Free Realms Trading Card Game lobby." },
		{ 451603, "Bry's Tournament" },
		{ 451612, "Practice using Bry's tournament deck." },
		{ 451604, "Bry's Trick" },
		{ 451613, "Practice using Bry's trick deck." },
		{ 451602, "Tutorial" },
		{ 451614, "Learn how to play the Free Realms Trading Card Game." }
	};

	private static readonly string[] CandidateClientRoots = new string[4] { "E:\\Free Realms\\Free Realms\\Servers\\Kaine's Server\\Client", "C:\\Users\\Public\\Documents\\OSFRLauncher\\Servers\\KainesOnlineServer\\Client", "C:\\Users\\seanm\\AppData\\Local\\OSFRLauncher\\Servers\\EDITz's Server\\Client", "E:\\Free Realms\\Free Realms\\Servers\\OS Free Realms\\Client" };

	private const int TcgLobbyOriginalDisplayNameId = 1320287513;

	private const int TcgLobbyOriginalDisplayDescriptionId = 903298991;

	private const int TreasureWarDisplayNameId = 1689170901;

	private const int TreasureWarDisplayDescriptionId = -727758913;

	private const int BryTournamentOriginalDisplayNameId = -1171390943;

	private const int BryTournamentOriginalDisplayDescriptionId = -903091888;

	private const int BryTrickOriginalDisplayNameId = 647264565;

	private const int BryTrickOriginalDisplayDescriptionId = 90954707;

	private const int TcgTutorialOriginalDisplayNameId = -711660947;

	private const int TcgTutorialOriginalDisplayDescriptionId = -1608897101;

	public const int TcgLobbyTitleCodeStringId = 3388;

	public const int TcgLobbyDescriptionCodeStringId = 401571;

	public const int TcgMainLobbyTitleStringId = 451041;

	public const int TcgMainLobbyDescriptionStringId = 451611;

	public const int BryTournamentTitleCodeStringId = 451603;

	public const int BryTournamentDescriptionCodeStringId = 451612;

	public const int BryTrickTitleCodeStringId = 451604;

	public const int BryTrickDescriptionCodeStringId = 451613;

	public const int TcgTutorialTitleCodeStringId = 451602;

	public const int TcgTutorialDescriptionCodeStringId = 451614;

	public const int PoeTournamentDeckTitleCodeStringId = 451499;

	public const int PoeTournamentDeckDescriptionCodeStringId = 451509;

	private const int TcgLobbyDisplayNameId = 451041;

	private const int TcgLobbyDisplayDescriptionId = 451611;

	private const int BryTournamentDisplayNameId = 451603;

	private const int BryTournamentDisplayDescriptionId = 451612;

	private const int BryTrickDisplayNameId = 451604;

	private const int BryTrickDisplayDescriptionId = 451613;

	private const int TcgTutorialDisplayNameId = 451602;

	private const int TcgTutorialDisplayDescriptionId = 451614;

	public const int PoeTournamentDeckActivityId = 499;

	public const int PoeTournamentDeckMiniGameDataId = 507;

	public const int PoeTournamentDeckOriginalDisplayNameId = -182711292;

	public const int PoeTournamentDeckOriginalDisplayDescriptionId = -105989495;

	public const int PoeTournamentDeckDisplayNameId = 451499;

	public const int PoeTournamentDeckDisplayDescriptionId = 451509;

	public const int PoeTournamentDeckIconId = 9870;

	public const string PoeTournamentDeckDetailImageFileName = "poes_tournament_detail.dds";

	public const string PoeTournamentDeckThumbnailImageFileName = "poes_tournament_thumb.dds";

	private static readonly ActivityRow[] TcgDefaultDetailRows = new ActivityRow[4]
	{
		new ActivityRow(41, 1, 41, 451041, 451611, 9867, 1, 1, 2, "tcg_lobby_detail.dds", "tcg_lobby_thumb.dds", "Main Lobby"),
		new ActivityRow(603, 1, 603, 451603, 451612, 9870, 2, 1, 2, "brys_tournament_detail.dds", "brys_tournament_thumb.dds", "Bry's Tournament"),
		new ActivityRow(604, 1, 604, 451604, 451613, 9870, 3, 1, 2, "brys_trick_detail.dds", "brys_trick_thumb.dds", "Bry's Trick"),
		new ActivityRow(602, 1, 602, 451602, 451614, 9870, 4, 1, 2, "tcg_tutorial_detail.dds", "tcg_tutorial_thumb.dds", "Tutorial")
	};

	private static readonly ActivityRow PoeTournamentDeckRow = new ActivityRow(499, 1, 507, 451499, 451509, 9870, 5, 1, 2, "poes_tournament_detail.dds", "poes_tournament_thumb.dds", "Poe's Tournament Deck");

	public static void SendPatch(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		SendDatasourceAndPopulate(connection, worldTunnel, playerGuid, reason);
	}

	public static void SendDatasourceOnly(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		SendDatasourceOnly(connection, worldTunnel, playerGuid, reason, 11);
	}

	public static void SendDatasourceOnly(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		if (!TrySuppressPacketReadyUiOverride(playerGuid, worldTunnel, reason, reason, activityCategoryId))
		{
			SendPatch(connection, worldTunnel, playerGuid, reason, populateDetail: false, NormalizeActivityCategoryId(activityCategoryId));
		}
	}

	public static void SendWarmDatasourcePreload(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string sourceEvent)
	{
		int num = 11;
		if (!TrySuppressPacketReadyUiOverride(playerGuid, worldTunnel, reason, reason, num))
		{
			if (playerGuid != 0L)
			{
				FirstSelectPopulateRetryScheduledPlayers.TryRemove(playerGuid, out var _);
			}
			SendDatasourceOnly(connection, worldTunnel, playerGuid, reason, num);
			MinigameDiagnosticsLog.Info("TcgDetailDatasourceWarmPreloadSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("browserV2TileId", 0), MinigameDiagnosticsLog.Field("selectedGameCategoryId", 0), MinigameDiagnosticsLog.Field("categoryId", num), MinigameDiagnosticsLog.Field("datasourceName", GetActivityCategoryDatasource(num)), MinigameDiagnosticsLog.Field("opcode", 167), MinigameDiagnosticsLog.Field("opcode167RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("rowIds", BuildDetailIdList(TcgDefaultDetailRows)), MinigameDiagnosticsLog.Field("rowNames", BuildDetailNameList(TcgDefaultDetailRows)), MinigameDiagnosticsLog.Field("didCallMinigameDetailShow", false), MinigameDiagnosticsLog.Field("didCallMinigameDetailPopulate", false), MinigameDiagnosticsLog.Field("didExposeOuterGridRows", false), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		}
	}

	public static void SendDatasourceAndPopulate(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		SendDatasourceAndPopulate(connection, worldTunnel, playerGuid, reason, 11);
	}

	public static void SendDatasourceAndPopulate(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		if (!TrySuppressPacketReadyUiOverride(playerGuid, worldTunnel, reason, reason, activityCategoryId))
		{
			SendPatch(connection, worldTunnel, playerGuid, reason, populateDetail: true, NormalizeActivityCategoryId(activityCategoryId));
		}
	}

	public static void SendDatasourceAndPopulateAfterDatasourceDelay(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		if (!TrySuppressPacketReadyUiOverride(playerGuid, worldTunnel, reason, reason, activityCategoryId))
		{
			SendShowDatasourceAndPopulate(connection, worldTunnel, playerGuid, reason, NormalizeActivityCategoryId(activityCategoryId));
		}
	}

	public static bool IsTcgDetailCategory(int activityCategoryId)
	{
		if (activityCategoryId != 11 && activityCategoryId != 26 && activityCategoryId != 23 && activityCategoryId != 24)
		{
			return activityCategoryId == 727;
		}
		return true;
	}

	private static bool TrySuppressPacketReadyUiOverride(ulong playerGuid, bool worldTunnel, string reason, string source, int activityCategoryId)
	{
		if (NormalizeActivityCategoryId(activityCategoryId) != 11 || !TryGetPacketReadyUiOverrideReason(reason, source, out var suppressionReason))
		{
			return false;
		}
		return PacketReadyActivityDefinitions.TrySuppressOldTcgOverride("TcgDetailUiPatch", playerGuid, worldTunnel, suppressionReason, string.IsNullOrWhiteSpace(source) ? reason : source, 11, "41,603,604,602");
	}

	private static bool TryGetPacketReadyUiOverrideReason(string reason, string source, out string suppressionReason)
	{
		if (ContainsPreloadOrQueueToken(reason) || ContainsPreloadOrQueueToken(source))
		{
			suppressionReason = "MenuDatasourcePreload";
			return true;
		}
		if (ContainsBrowserV2CategoryDetailToken(reason) || ContainsBrowserV2CategoryDetailToken(source))
		{
			suppressionReason = "BrowserV2CategoryDetail";
			return true;
		}
		suppressionReason = string.Empty;
		return false;
	}

	private static bool ContainsPreloadOrQueueToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (value.IndexOf("preload", StringComparison.OrdinalIgnoreCase) < 0 && value.IndexOf("list-queues", StringComparison.OrdinalIgnoreCase) < 0 && value.IndexOf("menu-datasource", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return value.IndexOf("BrowserV2:ShowGames", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private static bool ContainsBrowserV2CategoryDetailToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (value.IndexOf("browser-v2", StringComparison.OrdinalIgnoreCase) < 0 && value.IndexOf("BrowserV2:SelectGame", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return value.IndexOf("BrowserV2:ShowGamesAfterSelect", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private static void SendPatch(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, bool populateDetail, int activityCategoryId, int populateDelayMs = 0)
	{
		bool flag = populateDetail && populateDelayMs > 0;
		if (populateDetail)
		{
			MiniGamePacketHandler.SendTradingCardMiniGameGroupInfo(connection, worldTunnel, playerGuid, reason + "-group-info-before-populate");
		}
		SendActivityList(connection, worldTunnel, playerGuid, reason, populateDetail, activityCategoryId);
		if (populateDetail && !flag)
		{
			SendMinigameDetailItem(connection, worldTunnel, playerGuid, reason, activityCategoryId);
		}
		if (worldTunnel)
		{
			string text = reason + "-client-tunnel-mirror";
			if (populateDetail)
			{
				MiniGamePacketHandler.SendTradingCardMiniGameGroupInfo(connection, worldTunnel: false, playerGuid, text + "-group-info-before-populate");
			}
			SendActivityList(connection, worldTunnel: false, playerGuid, text, populateDetail, activityCategoryId);
			if (populateDetail && !flag)
			{
				SendMinigameDetailItem(connection, worldTunnel: false, playerGuid, text, activityCategoryId);
			}
		}
		if (flag)
		{
			ScheduleMinigameDetailPopulateAfterDatasource(connection, worldTunnel, playerGuid, reason, activityCategoryId, populateDelayMs);
		}
	}

	private static void SendShowDatasourceAndPopulate(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		bool flag = ShouldScheduleFirstSelectPopulateRetry(playerGuid);
		MinigameDiagnosticsLog.Info("TcgDetailShowDatasourcePopulateSequenceStarted", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("opcode167RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", (activityCategoryId == 11) ? TcgDefaultDetailRows.Length : 0), MinigameDiagnosticsLog.Field("delayMs", 0), MinigameDiagnosticsLog.Field("firstTcgSelectInSession", flag), MinigameDiagnosticsLog.Field("commandOrder", "1 MiniGameGroupInfo, 2 opcode167 Activities.Category11 refresh, 3 MinigameDetail:Show, 4 MinigameDetail:Populate"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		MinigameDiagnosticsLog.Info("TcgDetailFirstSelectPopulateAttempt", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("firstTcgSelectInSession", flag), MinigameDiagnosticsLog.Field("opcode167RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", (activityCategoryId == 11) ? TcgDefaultDetailRows.Length : 0), MinigameDiagnosticsLog.Field("rowIds", BuildDetailIdList(TcgDefaultDetailRows)), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		MiniGamePacketHandler.SendTradingCardMiniGameGroupInfo(connection, worldTunnel, playerGuid, reason + "-group-info-before-show");
		SendActivityList(connection, worldTunnel, playerGuid, reason + "-before-show", populateDetail: true, activityCategoryId);
		if (worldTunnel)
		{
			string text = reason + "-client-tunnel-mirror-before-show";
			MiniGamePacketHandler.SendTradingCardMiniGameGroupInfo(connection, worldTunnel: false, playerGuid, text + "-group-info-before-show");
			SendActivityList(connection, worldTunnel: false, playerGuid, text, populateDetail: true, activityCategoryId);
		}
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-show-after-datasource", "MinigameDetail:Show", activityCategoryId);
		if (worldTunnel)
		{
			SendUiIntScript(connection, false, playerGuid, reason + "-client-tunnel-mirror-show-after-datasource", "MinigameDetail:Show", activityCategoryId);
		}
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-populate-after-datasource", "MinigameDetail:Populate", activityCategoryId);
		SendTcgLiteralMinigameDetailOverride(connection, worldTunnel, playerGuid, reason + "-populate-after-datasource", activityCategoryId);
		LogMinigameDetailPopulateSent(playerGuid, worldTunnel, reason + "-populate-after-datasource", activityCategoryId);
		if (worldTunnel)
		{
			SendUiIntScript(connection, false, playerGuid, reason + "-client-tunnel-mirror-populate-after-datasource", "MinigameDetail:Populate", activityCategoryId);
			SendTcgLiteralMinigameDetailOverride(connection, worldTunnel: false, playerGuid, reason + "-client-tunnel-mirror-populate-after-datasource", activityCategoryId);
			LogMinigameDetailPopulateSent(playerGuid, worldTunnel: false, reason + "-client-tunnel-mirror-populate-after-datasource", activityCategoryId);
		}
		if (flag)
		{
			ScheduleFirstSelectPopulateRetry(connection, worldTunnel, playerGuid, reason, activityCategoryId);
		}
		MinigameDiagnosticsLog.Info("TcgDetailShowDatasourcePopulateSequenceSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("opcode167RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", (activityCategoryId == 11) ? TcgDefaultDetailRows.Length : 0), MinigameDiagnosticsLog.Field("datasourceName", GetActivityCategoryDatasource(activityCategoryId)), MinigameDiagnosticsLog.Field("rowIds", BuildDetailIdList(TcgDefaultDetailRows)), MinigameDiagnosticsLog.Field("firstTcgSelectInSession", flag), MinigameDiagnosticsLog.Field("populateRetryScheduled", flag), MinigameDiagnosticsLog.Field("populateRetryDelayMs", flag ? 200 : 0), MinigameDiagnosticsLog.Field("commandOrder", "datasource refresh before MinigameDetail:Show; MinigameDetail:Populate after Show"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
	}

	private static bool ShouldScheduleFirstSelectPopulateRetry(ulong playerGuid)
	{
		if (playerGuid == 0L)
		{
			return false;
		}
		return FirstSelectPopulateRetryScheduledPlayers.TryAdd(playerGuid, 1);
	}

	private static void ScheduleFirstSelectPopulateRetry(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		MinigameDiagnosticsLog.Info("TcgDetailFirstSelectPopulateRetry", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("stage", "scheduled"), MinigameDiagnosticsLog.Field("delayMs", 200), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(200).ConfigureAwait(continueOnCapturedContext: false);
				MinigameDiagnosticsLog.Info("TcgDetailFirstSelectPopulateRetry", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("stage", "fired"), MinigameDiagnosticsLog.Field("delayMs", 200), MinigameDiagnosticsLog.Field("script", "MinigameDetail:Populate"), MinigameDiagnosticsLog.Field("params", activityCategoryId), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
				SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-first-select-populate-retry", "MinigameDetail:Populate", activityCategoryId);
				if (worldTunnel)
				{
					SendUiIntScript(connection, false, playerGuid, reason + "-client-tunnel-mirror-first-select-populate-retry", "MinigameDetail:Populate", activityCategoryId);
				}
			}
			catch (Exception ex)
			{
				MinigameDiagnosticsLog.Warn("TcgDetailFirstSelectPopulateRetryFailed", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("delayMs", 200), MinigameDiagnosticsLog.Field("exception", ex.Message), MinigameDiagnosticsLog.Field("didLaunch", false));
			}
		});
	}

	private static void ScheduleMinigameDetailPopulateAfterDatasource(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId, int populateDelayMs)
	{
		MinigameDiagnosticsLog.Info("TcgDetailPopulateAfterDatasourceScheduled", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("delayMs", populateDelayMs), MinigameDiagnosticsLog.Field("opcode167RowCount", TcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", (activityCategoryId == 11) ? TcgDefaultDetailRows.Length : 0), MinigameDiagnosticsLog.Field("commandOrder", "1 MiniGameGroupInfo, 2 opcode167 Activities.Category11, 3 delayed MinigameDetail:Show/Populate"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(populateDelayMs).ConfigureAwait(continueOnCapturedContext: false);
				MinigameDiagnosticsLog.Info("TcgDetailPopulateAfterDatasourceFired", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("delayMs", populateDelayMs), MinigameDiagnosticsLog.Field("commandOrder", "3 delayed MinigameDetail:Show/Populate"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
				SendMinigameDetailItem(connection, worldTunnel, playerGuid, reason + "-delayed-populate", activityCategoryId);
				if (worldTunnel)
				{
					SendMinigameDetailItem(connection, worldTunnel: false, playerGuid, reason + "-client-tunnel-mirror-delayed-populate", activityCategoryId);
				}
			}
			catch (Exception ex)
			{
				MinigameDiagnosticsLog.Warn("TcgDetailPopulateAfterDatasourceFailed", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("delayMs", populateDelayMs), MinigameDiagnosticsLog.Field("exception", ex.Message), MinigameDiagnosticsLog.Field("didLaunch", false));
			}
		});
	}

	private static void SendActivityList(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, bool populateDetail, int activityCategoryId)
	{
		if (TrySuppressPacketReadyUiOverride(playerGuid, worldTunnel, reason, reason, activityCategoryId))
		{
			return;
		}
		ActivityRow[] visibleTcgDetailRows = GetVisibleTcgDetailRows(connection, worldTunnel, playerGuid, reason);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)167);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)1);
		packetWriter.Write(1);
		packetWriter.Write(visibleTcgDetailRows.Length);
		for (int i = 0; i < visibleTcgDetailRows.Length; i++)
		{
			ActivityRow row = visibleTcgDetailRows[i];
			LogActivityWireRow(connection, worldTunnel, playerGuid, reason, populateDetail, i, row, activityCategoryId);
			WriteActivity(packetWriter, row, activityCategoryId);
		}
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("TcgCategoryDataSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", populateDetail ? "Trading Card Game" : string.Empty), MinigameDiagnosticsLog.Field("selectedId", populateDetail ? 727 : 0), MinigameDiagnosticsLog.Field("activityId", populateDetail ? 24 : 0), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", populateDetail ? ("BrowserV2:SelectGame(" + activityCategoryId + ")") : "MenuDatasourcePreload"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("rows", visibleTcgDetailRows.Length), MinigameDiagnosticsLog.Field("activityListUpdateMode", 1), MinigameDiagnosticsLog.Field("activityListHeaderOrder", "opcode167,activityFamily=1,subtype=1,updateMode,rowCount"), MinigameDiagnosticsLog.Field("categoryDatasource", GetActivityCategoryDatasource(activityCategoryId)), MinigameDiagnosticsLog.Field("nativeParserEvidence", "FreeRealms.exe FUN_00be63e0 parses raw ClientActivityDefinition slots, but MinigameDetail.lua reads Activities.Category through FUN_00cfc180. The datasource getter maps NameId to native +0x28 and DescriptionId to +0x2c, so the packet must place text tokens in those getter slots."), MinigameDiagnosticsLog.Field("group36LinkGuidIds", BuildDetailIdList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("detailGroupLinkGuidIds", BuildDetailGroupLinkAliasIdList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("detailTargetMiniGameDataIds", BuildDetailTargetIdList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("detailGroupLinkAliasIds", "none; wire ids are native target row ids"), MinigameDiagnosticsLog.Field("detailNames", BuildDetailNameList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("primaryDetailRowId", 41), MinigameDiagnosticsLog.Field("primaryDetailTargetMiniGameDataId", 41), MinigameDiagnosticsLog.Field("primaryDetailGroupLinkAliasId", 41), MinigameDiagnosticsLog.Field("excludedCategory11LinkIds", "Treasure War row 39 is not part of the default TCG detail carousel; Poe row 499/507 is gated off until a real unlock condition is known."), MinigameDiagnosticsLog.Field("poeTournamentActivityId", 499), MinigameDiagnosticsLog.Field("poeTournamentMiniGameDataId", 507), MinigameDiagnosticsLog.Field("poeTournamentGate", "disabled-unknown-unlock-condition"), MinigameDiagnosticsLog.Field("acceptedTreasureWarAliasCategoryId", 26), MinigameDiagnosticsLog.Field("normalizedDetailCategoryId", 11), MinigameDiagnosticsLog.Field("nativeLaunchMiniGameId", 727), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("nativeNameId", 3388), MinigameDiagnosticsLog.Field("nativeDescriptionId", 401571), MinigameDiagnosticsLog.Field("nativeImageSetId", 7589), MinigameDiagnosticsLog.Field("nativeDetailImage", "tcg_lobby_detail.dds"), MinigameDiagnosticsLog.Field("nativeLuaClass", "TradingCardMain"), MinigameDiagnosticsLog.Field("nativePackageName", "game_tcg"), MinigameDiagnosticsLog.Field("populateDetail", populateDetail), MinigameDiagnosticsLog.Field("rowSchema", "id,appSystemId,category,imageSetId,activityPositionId,displayNameId,displayDescriptionId,nameId,descriptionId,serverType,canPlayerJoinByte,preferredRequirementId,featuredEntryCount,tutorialActivityId,membersOnly,detailImageFilename,thumbnailImageFilename,difficulty,unknown3BackingMiniGameDataId,mysteryChestId,mysteryChestIcon"), MinigameDiagnosticsLog.Field("previousMisalignedRowSchema", "id,appSystemId,category,displayNameId,displayDescriptionId,nameId,descriptionId,imageSetId,activityPositionId,canPlayerJoinInt,isFeaturedByte,preferredRequirementId,featuredEntryCount,tutorialActivityId,membersOnly,detailImageFilename,thumbnailImageFilename,difficulty,featuredRewardTooltipStringId,mysteryChestIcon,chestId"), MinigameDiagnosticsLog.Field("rootCause", "The row order is left unchanged. The row source now separates always-visible TCG detail rows from optional Poe challenge rows; Poe is not sent until a real account/quest/progression unlock can be proven."), MinigameDiagnosticsLog.Field("fix", "Keep the native category-11 Populate path and working image filenames. Send only Lobby, Bry Tournament, Bry Trick, and Tutorial by default; record Poe metadata but keep it disabled instead of globally unlocking it."), MinigameDiagnosticsLog.Field("diagnosis", "This keeps the native MinigameDetail:Show/Populate route, then applies a scoped post-populate MinigameDetail:addHorzItem_lua literal datasource refresh for category 11 only. It changes only Games/minigame detail UI metadata and does not launch TCG."), MinigameDiagnosticsLog.Field("localeFinding", "The wire carousel row ids remain the traced 451xxx values, but the live client does not resolve those CodeStringMappings keys to intended minigame text. Do not count them as resolved unless the post-populate literal datasource is observed in the live SWF path."), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		MinigameDiagnosticsLog.Info("TcgDetailDatasourcePreloadSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("sourceEvent", populateDetail ? ("BrowserV2:SelectGame(" + activityCategoryId + ")") : "MenuDatasourcePreload"), MinigameDiagnosticsLog.Field("browserV2TileId", activityCategoryId), MinigameDiagnosticsLog.Field("selectedGameCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("opcode", 167), MinigameDiagnosticsLog.Field("opcode167RowCount", visibleTcgDetailRows.Length), MinigameDiagnosticsLog.Field("activitiesCategory11RowCount", (activityCategoryId == 11) ? visibleTcgDetailRows.Length : 0), MinigameDiagnosticsLog.Field("categoryDatasource", GetActivityCategoryDatasource(activityCategoryId)), MinigameDiagnosticsLog.Field("rowIds", BuildDetailIdList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("rowNames", BuildDetailNameList(visibleTcgDetailRows)), MinigameDiagnosticsLog.Field("commandOrder", BuildDatasourceCommandOrder(populateDetail, reason)), MinigameDiagnosticsLog.Field("sentAtUtc", DateTime.UtcNow.ToString("O")), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
	}

	public static bool ShouldIncludePoeTournamentDeck(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string caller)
	{
		MinigameDiagnosticsLog.Info("TcgPoeTournamentGate", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("caller", caller), MinigameDiagnosticsLog.Field("result", "disabled"), MinigameDiagnosticsLog.Field("gateState", "unknown"), MinigameDiagnosticsLog.Field("activityId", 499), MinigameDiagnosticsLog.Field("miniGameDataId", 507), MinigameDiagnosticsLog.Field("nameIdSigned", 451499), MinigameDiagnosticsLog.Field("nameIdUnsigned", Unsigned(451499)), MinigameDiagnosticsLog.Field("descriptionIdSigned", 451509), MinigameDiagnosticsLog.Field("descriptionIdUnsigned", Unsigned(451509)), MinigameDiagnosticsLog.Field("detailImage", "poes_tournament_detail.dds"), MinigameDiagnosticsLog.Field("thumbnailImage", "poes_tournament_thumb.dds"), MinigameDiagnosticsLog.Field("evidence", "Sanctuary-minigame ClientActivityDefinitions has Poe activity 499 backed by 507 with poes_tournament images; active Kaine locale resolves Poe title through signed id -182711292/uint 4112256004 and description through signed id -105989495/uint 4188977801."), MinigameDiagnosticsLog.Field("unlockFinding", "No quest/progression/unlock storage or PreferredRequirementId is present in the current gateway path, so Poe stays hidden by default."));
		return false;
	}

	private static ActivityRow[] GetVisibleTcgDetailRows(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		if (!ShouldIncludePoeTournamentDeck(connection, worldTunnel, playerGuid, reason, "opcode167-activity-list"))
		{
			return TcgDefaultDetailRows;
		}
		return new List<ActivityRow>(TcgDefaultDetailRows) { PoeTournamentDeckRow }.ToArray();
	}

	private static void LogActivityWireRow(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, bool populateDetail, int rowIndex, ActivityRow row, int activityCategoryId)
	{
		int originalDisplayNameId = GetOriginalDisplayNameId(row.MiniGameDataId);
		int originalDisplayDescriptionId = GetOriginalDisplayDescriptionId(row.MiniGameDataId);
		LocaleLookupResult localeLookupResult = ResolveLocaleTokenForDiagnostics(originalDisplayNameId);
		LocaleLookupResult localeLookupResult2 = ResolveLocaleTokenForDiagnostics(originalDisplayDescriptionId);
		LocaleLookupResult localeLookupResult3 = ResolveUiGetStringByIdForDiagnostics(row.DisplayNameId);
		LocaleLookupResult localeLookupResult4 = ResolveUiGetStringByIdForDiagnostics(row.DisplayDescriptionId);
		MinigameDiagnosticsLog.Info("TcgActivityWireRow", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", populateDetail ? "Trading Card Game" : string.Empty), MinigameDiagnosticsLog.Field("selectedId", populateDetail ? 727 : 0), MinigameDiagnosticsLog.Field("activityId", populateDetail ? 24 : 0), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", populateDetail ? ("BrowserV2:SelectGame(" + activityCategoryId + ")") : "MenuDatasourcePreload"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.Field("rowIndex", rowIndex), MinigameDiagnosticsLog.Field("wireOrder", "id,appSystemId,category,imageSetId,activityPositionId,displayNameId,displayDescriptionId,nameId,descriptionId,serverType,canPlayerJoinByte,preferredRequirementId,featuredEntryCount,tutorialActivityId,membersOnly,detailImageFilename,thumbnailImageFilename,difficulty,unknown3BackingMiniGameDataId,mysteryChestId,mysteryChestIcon"), MinigameDiagnosticsLog.Field("wireSignedValuesBeforeStrings", BuildActivityWireSignedValues(row, activityCategoryId)), MinigameDiagnosticsLog.Field("rowIdSigned", row.Id), MinigameDiagnosticsLog.Field("rowIdUnsigned", Unsigned(row.Id)), MinigameDiagnosticsLog.Field("rowIdHex", Hex(row.Id)), MinigameDiagnosticsLog.Field("wireGuidSigned", row.Id), MinigameDiagnosticsLog.Field("wireGuidUnsigned", Unsigned(row.Id)), MinigameDiagnosticsLog.Field("wireGuidHex", Hex(row.Id)), MinigameDiagnosticsLog.Field("wireGuidKind", "ClientActivityDefinition id / native TCG detail row id"), MinigameDiagnosticsLog.Field("appSystemIdSigned", row.AppSystemId), MinigameDiagnosticsLog.Field("appSystemIdUnsigned", Unsigned(row.AppSystemId)), MinigameDiagnosticsLog.Field("appSystemIdHex", Hex(row.AppSystemId)), MinigameDiagnosticsLog.Field("targetMiniGameDataIdSigned", row.MiniGameDataId), MinigameDiagnosticsLog.Field("targetMiniGameDataIdUnsigned", Unsigned(row.MiniGameDataId)), MinigameDiagnosticsLog.Field("targetMiniGameDataIdHex", Hex(row.MiniGameDataId)), MinigameDiagnosticsLog.Field("groupLinkAliasSigned", GetGroupLinkAliasId(row.MiniGameDataId)), MinigameDiagnosticsLog.Field("group36LinkTargetAliasSigned", GetGroupLinkAliasId(row.MiniGameDataId)), MinigameDiagnosticsLog.Field("identityFinding", "ActivityCategories row 11 opens MiniGameGroupData row 36. The default visible carousel rows are native MiniGameData ids 41,603,604,602; Poe is a gated candidate at activity 499 backed by 507. Alias ids 611..615 collide with existing client MiniGameData rows and can leak stale/default fallback text into Browser_V2."), MinigameDiagnosticsLog.Field("nativeParseFinding", "FreeRealms.exe FUN_00be63e0 parses raw ClientActivityDefinition slots, but MinigameDetail.lua reads Activities.Category through FUN_00cfc180. The datasource getter maps NameId to native +0x28 and DescriptionId to +0x2c, so the packet must place text tokens in those getter slots."), MinigameDiagnosticsLog.Field("localLaunchActivityId", 24), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("nativeLaunchMiniGameId", 727), MinigameDiagnosticsLog.Field("nativeNameId", 3388), MinigameDiagnosticsLog.Field("nativeDescriptionId", 401571), MinigameDiagnosticsLog.Field("nativeImageSetId", 7589), MinigameDiagnosticsLog.Field("oldNameId", originalDisplayNameId), MinigameDiagnosticsLog.Field("oldNameIdUnsigned", Unsigned(originalDisplayNameId)), MinigameDiagnosticsLog.Field("oldNameLocaleResolved", localeLookupResult.Resolved), MinigameDiagnosticsLog.Field("oldNameLocaleLookupId", localeLookupResult.LookupId), MinigameDiagnosticsLog.Field("oldNameLocaleString", localeLookupResult.Text), MinigameDiagnosticsLog.Field("newNameId", row.DisplayNameId), MinigameDiagnosticsLog.Field("oldDescriptionId", originalDisplayDescriptionId), MinigameDiagnosticsLog.Field("oldDescriptionIdUnsigned", Unsigned(originalDisplayDescriptionId)), MinigameDiagnosticsLog.Field("oldDescriptionLocaleResolved", localeLookupResult2.Resolved), MinigameDiagnosticsLog.Field("oldDescriptionLocaleLookupId", localeLookupResult2.LookupId), MinigameDiagnosticsLog.Field("oldDescriptionLocaleString", localeLookupResult2.Text), MinigameDiagnosticsLog.Field("newDescriptionId", row.DisplayDescriptionId), MinigameDiagnosticsLog.Field("codeStringMappingExists", localeLookupResult3.CodeStringMappingExists && localeLookupResult4.CodeStringMappingExists), MinigameDiagnosticsLog.Field("displayNameSigned", row.DisplayNameId), MinigameDiagnosticsLog.Field("displayNameUnsigned", Unsigned(row.DisplayNameId)), MinigameDiagnosticsLog.Field("displayNameHex", Hex(row.DisplayNameId)), MinigameDiagnosticsLog.Field("displayNameResolved", localeLookupResult3.Resolved), MinigameDiagnosticsLog.Field("displayNameResolvedLookupId", localeLookupResult3.LookupId), MinigameDiagnosticsLog.Field("displayNameResolvedString", localeLookupResult3.Text), MinigameDiagnosticsLog.Field("displayNameResolvedSource", localeLookupResult3.Source), MinigameDiagnosticsLog.Field("displayNameCodeStringMappingExists", localeLookupResult3.CodeStringMappingExists), MinigameDiagnosticsLog.Field("displayNameCodeStringMappingKey", localeLookupResult3.CodeStringMappingKey), MinigameDiagnosticsLog.Field("displayNameUiGetStringByIdAllowed", localeLookupResult3.UiGetStringByIdAllowed), MinigameDiagnosticsLog.Field("displayDescriptionSigned", row.DisplayDescriptionId), MinigameDiagnosticsLog.Field("displayDescriptionUnsigned", Unsigned(row.DisplayDescriptionId)), MinigameDiagnosticsLog.Field("displayDescriptionHex", Hex(row.DisplayDescriptionId)), MinigameDiagnosticsLog.Field("displayDescriptionResolved", localeLookupResult4.Resolved), MinigameDiagnosticsLog.Field("displayDescriptionResolvedLookupId", localeLookupResult4.LookupId), MinigameDiagnosticsLog.Field("displayDescriptionResolvedString", localeLookupResult4.Text), MinigameDiagnosticsLog.Field("displayDescriptionResolvedSource", localeLookupResult4.Source), MinigameDiagnosticsLog.Field("displayDescriptionCodeStringMappingExists", localeLookupResult4.CodeStringMappingExists), MinigameDiagnosticsLog.Field("displayDescriptionCodeStringMappingKey", localeLookupResult4.CodeStringMappingKey), MinigameDiagnosticsLog.Field("displayDescriptionUiGetStringByIdAllowed", localeLookupResult4.UiGetStringByIdAllowed), MinigameDiagnosticsLog.Field("categorySigned", activityCategoryId), MinigameDiagnosticsLog.Field("categoryUnsigned", Unsigned(activityCategoryId)), MinigameDiagnosticsLog.Field("categoryHex", Hex(activityCategoryId)), MinigameDiagnosticsLog.Field("nameSigned", row.DisplayNameId), MinigameDiagnosticsLog.Field("nameUnsigned", Unsigned(row.DisplayNameId)), MinigameDiagnosticsLog.Field("nameHex", Hex(row.DisplayNameId)), MinigameDiagnosticsLog.Field("nameResolved", localeLookupResult3.Resolved), MinigameDiagnosticsLog.Field("nameResolvedLookupId", localeLookupResult3.LookupId), MinigameDiagnosticsLog.Field("nameResolvedString", localeLookupResult3.Text), MinigameDiagnosticsLog.Field("nameResolvedSource", localeLookupResult3.Source), MinigameDiagnosticsLog.Field("descriptionSigned", row.DisplayDescriptionId), MinigameDiagnosticsLog.Field("descriptionUnsigned", Unsigned(row.DisplayDescriptionId)), MinigameDiagnosticsLog.Field("descriptionHex", Hex(row.DisplayDescriptionId)), MinigameDiagnosticsLog.Field("descriptionResolved", localeLookupResult4.Resolved), MinigameDiagnosticsLog.Field("descriptionResolvedLookupId", localeLookupResult4.LookupId), MinigameDiagnosticsLog.Field("descriptionResolvedString", localeLookupResult4.Text), MinigameDiagnosticsLog.Field("descriptionResolvedSource", localeLookupResult4.Source), MinigameDiagnosticsLog.Field("finalNameExpectedByNative", localeLookupResult3.Text), MinigameDiagnosticsLog.Field("finalDescriptionExpectedByNative", localeLookupResult4.Text), MinigameDiagnosticsLog.Field("finalNameExpectedByUiGetStringById", localeLookupResult3.Text), MinigameDiagnosticsLog.Field("finalDescriptionExpectedByUiGetStringById", localeLookupResult4.Text), MinigameDiagnosticsLog.Field("finalStringPath", "opcode 167 row order and image slots are unchanged. MinigameDetail.lua passes Activities_col.NameId/DescriptionId to Ui.GetStringById, so row text tokens must be positive CodeStringMappings ids."), MinigameDiagnosticsLog.Field("plainTitle", row.DebugName), MinigameDiagnosticsLog.Field("plainTitleWireSlot", "diagnostic only; opcode 167 sends signed locale tokens, not literal strings"), MinigameDiagnosticsLog.Field("plainDescription", GetPlainDescription(row.MiniGameDataId)), MinigameDiagnosticsLog.Field("plainDescriptionWireSlot", "diagnostic only; opcode 167 sends signed locale tokens, not literal strings"), MinigameDiagnosticsLog.Field("localeTokenFormat", "old datasource tokens are signed/unsigned locale hashes; final wire text tokens are positive CodeStringMappings ids because Ui.GetStringById rejects negative ids and falls back for unmapped ids"), MinigameDiagnosticsLog.Field("iconSigned", row.IconId), MinigameDiagnosticsLog.Field("iconUnsigned", Unsigned(row.IconId)), MinigameDiagnosticsLog.Field("iconHex", Hex(row.IconId)), MinigameDiagnosticsLog.Field("imageSetIdSigned", row.IconId), MinigameDiagnosticsLog.Field("imageSetIdUnsigned", Unsigned(row.IconId)), MinigameDiagnosticsLog.Field("imageSetIdHex", Hex(row.IconId)), MinigameDiagnosticsLog.Field("iconWireSlot", "Activities_col.ImageSetId -> FUN_00cfc180 native +0x14"), MinigameDiagnosticsLog.Field("position", row.Position), MinigameDiagnosticsLog.Field("positionWireSlot", "Activities_col.ActivityPositionId -> FUN_00cfc180 native +0x18"), MinigameDiagnosticsLog.Field("difficulty", row.Difficulty), MinigameDiagnosticsLog.Field("difficultyWireSlot", "Activities_col.Difficulty -> FUN_00cfc180 native +0x60"), MinigameDiagnosticsLog.Field("canPlayerJoin", 1), MinigameDiagnosticsLog.Field("canPlayerJoinWireSlot", "Activities_col.CanPlayerJoin -> FUN_00cfc180 native +0x30 byte"), MinigameDiagnosticsLog.Field("serverType", row.ServerType), MinigameDiagnosticsLog.Field("serverTypeWireSlot", "ClientActivityDefinition.ServerType after DescriptionId"), MinigameDiagnosticsLog.Field("preferredRequirementId", 0), MinigameDiagnosticsLog.Field("featuredEntryCount", 0), MinigameDiagnosticsLog.Field("tutorialActivityId", 0), MinigameDiagnosticsLog.Field("membersOnlyByte", 0), MinigameDiagnosticsLog.Field("detailImage", row.DetailImage), MinigameDiagnosticsLog.Field("detailImagePublicAssetUrl", BuildPublicAssetUrlForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("detailImageExists", ClientResourceAssetExistsForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("detailImageLooseResource", ResolveClientResourceAssetForDiagnostics(row.DetailImage)), MinigameDiagnosticsLog.Field("thumbnailImage", row.ThumbnailImage), MinigameDiagnosticsLog.Field("thumbnailImagePublicAssetUrl", BuildPublicAssetUrlForDiagnostics(row.ThumbnailImage)), MinigameDiagnosticsLog.Field("thumbnailImageExists", ClientResourceAssetExistsForDiagnostics(row.ThumbnailImage)), MinigameDiagnosticsLog.Field("thumbnailImageLooseResource", ResolveClientResourceAssetForDiagnostics(row.ThumbnailImage)), MinigameDiagnosticsLog.Field("unknown3BackingMiniGameDataId", row.MiniGameDataId), MinigameDiagnosticsLog.Field("unknown3WireSlot", "ClientActivityDefinition.Unknown3 after Difficulty"), MinigameDiagnosticsLog.Field("mysteryChestIcon", 0), MinigameDiagnosticsLog.Field("chestId", 0), MinigameDiagnosticsLog.Field("debugName", row.DebugName));
	}

	private static void SendMinigameDetailItem(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		SendUiIntScript(connection, worldTunnel, playerGuid, reason, "MinigameDetail:Show", activityCategoryId);
		SendUiIntScript(connection, worldTunnel, playerGuid, reason, "MinigameDetail:Populate", activityCategoryId);
		SendTcgLiteralMinigameDetailOverride(connection, worldTunnel, playerGuid, reason, activityCategoryId);
		LogMinigameDetailPopulateSent(playerGuid, worldTunnel, reason, activityCategoryId);
	}

	private static void LogMinigameDetailPopulateSent(ulong playerGuid, bool worldTunnel, string reason, int activityCategoryId)
	{
		MinigameDiagnosticsLog.Info("GameSelected", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", 727), MinigameDiagnosticsLog.Field("activityId", 24), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "MinigameDetailPopulate"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		MinigameDiagnosticsLog.Info("GameDetailPanelUpdated", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", 727), MinigameDiagnosticsLog.Field("activityId", 24), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "MinigameDetailPopulate"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		MinigameDiagnosticsLog.Info("TcgMinigameDetailPopulateSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("selectedGame", "Trading Card Game"), MinigameDiagnosticsLog.Field("selectedId", 727), MinigameDiagnosticsLog.Field("activityId", 24), MinigameDiagnosticsLog.Field("gameKey", "tcg"), MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("miniGameGroupId", 36), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("nativeLaunchMiniGameId", 727), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("nativeNameId", 3388), MinigameDiagnosticsLog.Field("nativeDescriptionId", 401571), MinigameDiagnosticsLog.Field("nativeImageSetId", 7589), MinigameDiagnosticsLog.Field("nativeLuaClass", "TradingCardMain"), MinigameDiagnosticsLog.Field("nativePackageName", "game_tcg"), MinigameDiagnosticsLog.Field("handler", "TradingCardGameHandler"), MinigameDiagnosticsLog.Field("sourceEvent", "BrowserV2:SelectGame(" + activityCategoryId + ")"), MinigameDiagnosticsLog.Field("populateSkipped", false), MinigameDiagnosticsLog.Field("populateReason", "Native first-click sequence: MinigameDetail:Show establishes the detail panel, then opcode 167 Activities.Category11 rows are sent, then MinigameDetail:Populate(category) commits the carousel."), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
	}

	private static void SendTcgLiteralMinigameDetailOverride(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int activityCategoryId)
	{
		if (!EnableTcgLiteralOverride)
		{
			MinigameDiagnosticsLog.Info("TcgDetailLiteralOverrideSkipped", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("skipReason", "feature-flag-disabled"), MinigameDiagnosticsLog.Field("featureFlag", "EnableTcgLiteralOverride"), MinigameDiagnosticsLog.Field("featureFlagValue", EnableTcgLiteralOverride), MinigameDiagnosticsLog.Field("resetCommandSent", false), MinigameDiagnosticsLog.Field("rowCount", 0), MinigameDiagnosticsLog.Field("opcode167RowOrderChanged", false), MinigameDiagnosticsLog.Field("imageFieldsChanged", false), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		if (activityCategoryId != 11)
		{
			MinigameDiagnosticsLog.Info("TcgDetailLiteralOverrideSkipped", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("expectedCategoryId", 11), MinigameDiagnosticsLog.Field("skipReason", "scoped-to-category-11-only"), MinigameDiagnosticsLog.Field("didLaunch", false));
			return;
		}
		ActivityRow[] tcgDefaultDetailRows = TcgDefaultDetailRows;
		SendUiStringScript(connection, worldTunnel, playerGuid, reason + "-literal-override-reset", "MinigameDetail:items_mc.resetItems");
		MinigameDiagnosticsLog.Info("TcgDetailLiteralOverrideAttempt", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("scope", "MinigameDetail category 11 only"), MinigameDiagnosticsLog.Field("resetCommandSent", true), MinigameDiagnosticsLog.Field("resetMethod", "MinigameDetail:items_mc.resetItems"), MinigameDiagnosticsLog.Field("resetResult", "sent-not-acknowledged"), MinigameDiagnosticsLog.Field("rowCount", tcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("rowOrder", BuildDetailIdList(tcgDefaultDetailRows)), MinigameDiagnosticsLog.Field("packetShapeChanged", false), MinigameDiagnosticsLog.Field("opcode167RowOrderChanged", false), MinigameDiagnosticsLog.Field("imageFieldsChanged", false), MinigameDiagnosticsLog.Field("didLaunch", false));
		for (int i = 0; i < tcgDefaultDetailRows.Length; i++)
		{
			ActivityRow row = tcgDefaultDetailRows[i];
			string literalTitle = GetLiteralTitle(row);
			string literalDescription = GetLiteralDescription(row);
			SendUiStringScript(connection, worldTunnel, playerGuid, reason + "-literal-override-row", "MinigameDetail:addHorzItem_lua", (i + 1).ToString(), tcgDefaultDetailRows.Length.ToString(), row.Id.ToString(), row.IconId.ToString(), literalTitle, literalDescription, "1", "0", "0", row.DetailImage, row.ThumbnailImage, "1", row.Difficulty.ToString(), "Levels 1 - 5", row.Position.ToString(), "0");
			MinigameDiagnosticsLog.Info("TcgDetailLiteralOverrideAttempt", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("rowCount", tcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("rowIndex", i), MinigameDiagnosticsLog.Field("rowId", row.Id), MinigameDiagnosticsLog.Field("title", literalTitle), MinigameDiagnosticsLog.Field("description", literalDescription), MinigameDiagnosticsLog.Field("detailImage", row.DetailImage), MinigameDiagnosticsLog.Field("thumbnailImage", row.ThumbnailImage), MinigameDiagnosticsLog.Field("sourceEvent", "post-MinigameDetail:Populate(11) literal string override"), MinigameDiagnosticsLog.Field("didLaunch", false));
		}
		SendUiStringScript(connection, worldTunnel, playerGuid, reason + "-literal-override-done", "MinigameDetail:doneAddingToHorzItem_lua");
		MinigameDiagnosticsLog.Info("TcgDetailLiteralOverrideAttempt", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("resetMethod", "MinigameDetail:items_mc.resetItems"), MinigameDiagnosticsLog.Field("rowCount", tcgDefaultDetailRows.Length), MinigameDiagnosticsLog.Field("doneMethod", "MinigameDetail:doneAddingToHorzItem_lua"), MinigameDiagnosticsLog.Field("sourceEvent", "post-MinigameDetail:Populate(11) literal string override complete"), MinigameDiagnosticsLog.Field("didLaunch", false));
	}

	private static void SendTcgRuntimeStringOverrides(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason)
	{
		foreach (KeyValuePair<int, string> item in RuntimeTcgStringOverridesById)
		{
			SendUiStringScript(connection, worldTunnel, playerGuid, reason, "Ui:ReplaceString", item.Key.ToString(), item.Value);
			MinigameDiagnosticsLog.Info("TcgRuntimeStringOverrideSent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("stringId", item.Key), MinigameDiagnosticsLog.Field("stringValue", item.Value), MinigameDiagnosticsLog.Field("sourceEvent", "pre-Activities.Category11 datasource"), MinigameDiagnosticsLog.Field("script", "Ui:ReplaceString"), MinigameDiagnosticsLog.Field("stringPath", "native string table override before MinigameDetail:Populate; no SWF edit and no TCG gameplay packet change"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
		}
	}

	private static void SendUiIntScript(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string script, params int[] args)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(script);
		packetWriter.Write(args);
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("tcg-detail-int-script-sent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", script), MinigameDiagnosticsLog.Field("paramCount", args.Length), MinigameDiagnosticsLog.Field("params", string.Join(",", args)), MinigameDiagnosticsLog.Field("minigameDetailShowParams", (script == "MinigameDetail:Show") ? string.Join(",", args) : string.Empty), MinigameDiagnosticsLog.Field("minigameDetailPopulateParams", (script == "MinigameDetail:Populate") ? string.Join(",", args) : string.Empty), MinigameDiagnosticsLog.Field("sentAtUtc", DateTime.UtcNow.ToString("O")), MinigameDiagnosticsLog.Field("commandOrder", BuildUiCommandOrder(script, reason)), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static string BuildDatasourceCommandOrder(bool populateDetail, string reason)
	{
		if (!populateDetail)
		{
			return "datasource only; no MinigameDetail populate";
		}
		if (reason.IndexOf("after-show", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "datasource after MinigameDetail:Show and before MinigameDetail:Populate";
		}
		return "datasource before MinigameDetail:Show/Populate";
	}

	private static string BuildUiCommandOrder(string script, string reason)
	{
		if (script == "MinigameDetail:Show" && reason.IndexOf("show-before-datasource", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "MinigameDetail:Show before opcode167 datasource";
		}
		if (script == "MinigameDetail:Populate" && reason.IndexOf("populate-after-datasource", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "MinigameDetail:Populate after opcode167 datasource";
		}
		if (script == "MinigameDetail:Show")
		{
			return "MinigameDetail:Show after opcode167 datasource";
		}
		if (script == "MinigameDetail:Populate")
		{
			return "MinigameDetail:Populate after opcode167 datasource";
		}
		return "ui-int-script";
	}

	private static void SendUiStringScript(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string script, params string[] args)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)8);
		packetWriter.Write(script);
		packetWriter.Write(0);
		packetWriter.Write(args.Length);
		foreach (string text in args)
		{
			packetWriter.Write(text ?? string.Empty);
		}
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("tcg-detail-string-script-sent", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", script), MinigameDiagnosticsLog.Field("paramCount", args.Length), MinigameDiagnosticsLog.Field("params", string.Join("|", args)), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void WriteActivity(PacketWriter writer, ActivityRow row, int activityCategoryId)
	{
		writer.Write(row.Id);
		writer.Write(row.AppSystemId);
		writer.Write(activityCategoryId);
		writer.Write(row.IconId);
		writer.Write(row.Position);
		writer.Write(row.DisplayNameId);
		writer.Write(row.DisplayDescriptionId);
		writer.Write(row.DisplayNameId);
		writer.Write(row.DisplayDescriptionId);
		writer.Write(row.ServerType);
		writer.Write((byte)1);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write((byte)0);
		writer.Write(row.DetailImage);
		writer.Write(row.ThumbnailImage);
		writer.Write(row.Difficulty);
		writer.Write(row.MiniGameDataId);
		writer.Write(0);
		writer.Write(0);
	}

	private static string BuildDetailIdList(ActivityRow[] rows)
	{
		string text = string.Empty;
		for (int i = 0; i < rows.Length; i++)
		{
			ActivityRow activityRow = rows[i];
			text = ((text.Length == 0) ? activityRow.Id.ToString() : (text + "," + activityRow.Id));
		}
		return text;
	}

	private static string BuildDetailNameList(ActivityRow[] rows)
	{
		string text = string.Empty;
		for (int i = 0; i < rows.Length; i++)
		{
			ActivityRow activityRow = rows[i];
			text = ((text.Length == 0) ? activityRow.DebugName : (text + "," + activityRow.DebugName));
		}
		return text;
	}

	private static string BuildDetailTargetIdList(ActivityRow[] rows)
	{
		string text = string.Empty;
		for (int i = 0; i < rows.Length; i++)
		{
			ActivityRow activityRow = rows[i];
			text = ((text.Length == 0) ? activityRow.MiniGameDataId.ToString() : (text + "," + activityRow.MiniGameDataId));
		}
		return text;
	}

	private static string BuildDetailGroupLinkAliasIdList(ActivityRow[] rows)
	{
		string text = string.Empty;
		for (int i = 0; i < rows.Length; i++)
		{
			int groupLinkAliasId = GetGroupLinkAliasId(rows[i].MiniGameDataId);
			text = ((text.Length == 0) ? groupLinkAliasId.ToString() : (text + "," + groupLinkAliasId));
		}
		return text;
	}

	private static uint Unsigned(int value)
	{
		return (uint)value;
	}

	private static string Hex(int value)
	{
		return "0x" + Unsigned(value).ToString("X8");
	}

	private static int NormalizeActivityCategoryId(int activityCategoryId)
	{
		return 11;
	}

	private static string GetActivityCategoryDatasource(int activityCategoryId)
	{
		return "Activities.Category" + activityCategoryId;
	}

	private static string GetPlainDescription(int rowId)
	{
		return rowId switch
		{
			39 => "Play the exciting Treasure War card game! Battle your friends in quickplay mode, fight your way through the campaign mode, compete in tournaments, or manage your card collection.", 
			41 => "Enter the Free Realms Trading Card Game lobby.", 
			603 => "Practice using Bry's tournament deck.", 
			604 => "Practice using Bry's trick deck.", 
			602 => "Learn how to play the Free Realms Trading Card Game.", 
			507 => "As a Card Duelist; Beat Poe's Tournament Deck.", 
			_ => "Play the Free Realms Trading Card Game.", 
		};
	}

	private static string GetLiteralTitle(ActivityRow row)
	{
		switch (row.MiniGameDataId)
		{
		case 41:
			return "Main Lobby";
		case 603:
			return "Bry's Tournament";
		case 604:
			return "Bry's Trick";
		case 602:
			return "Tutorial";
		case 507:
			return "Poe's Tournament";
		default:
		{
			LocaleLookupResult localeLookupResult = ResolveLocaleTokenForDiagnostics(GetOriginalDisplayNameId(row.MiniGameDataId));
			if (!localeLookupResult.Resolved)
			{
				return row.DebugName;
			}
			return localeLookupResult.Text;
		}
		}
	}

	private static string GetLiteralDescription(ActivityRow row)
	{
		LocaleLookupResult localeLookupResult = ResolveLocaleTokenForDiagnostics(GetOriginalDisplayDescriptionId(row.MiniGameDataId));
		if (!localeLookupResult.Resolved)
		{
			return GetPlainDescription(row.MiniGameDataId);
		}
		return localeLookupResult.Text;
	}

	private static int GetGroupLinkAliasId(int rowId)
	{
		return rowId;
	}

	private static int GetOriginalDisplayNameId(int rowId)
	{
		return rowId switch
		{
			41 => 1320287513, 
			603 => -1171390943, 
			604 => 647264565, 
			602 => -711660947, 
			507 => -182711292, 
			_ => rowId, 
		};
	}

	private static int GetOriginalDisplayDescriptionId(int rowId)
	{
		return rowId switch
		{
			41 => 903298991, 
			603 => -903091888, 
			604 => 90954707, 
			602 => -1608897101, 
			507 => -105989495, 
			_ => 0, 
		};
	}

	private static string BuildActivityWireSignedValues(ActivityRow row, int activityCategoryId)
	{
		return string.Join(",", row.Id, row.AppSystemId, activityCategoryId, row.IconId, row.Position, row.DisplayNameId, row.DisplayDescriptionId, row.DisplayNameId, row.DisplayDescriptionId, row.ServerType, "canPlayerJoinByte=1", "preferredRequirementId=0", "featuredEntryCount=0", "tutorialActivityId=0", "membersOnlyByte=0", row.DetailImage, row.ThumbnailImage, row.Difficulty, row.MiniGameDataId, "mysteryChestId=0", "mysteryChestIcon=0");
	}

	internal static LocaleLookupResult ResolveLocaleTokenForDiagnostics(int id)
	{
		EnsureLocaleLoaded();
		uint num = Unsigned(id);
		if (_localeStrings != null && _localeStrings.TryGetValue(num, out var value))
		{
			return new LocaleLookupResult(resolved: true, value, _localeSource, num);
		}
		return new LocaleLookupResult(resolved: false, "##" + id, _localeSource, num);
	}

	internal static LocaleLookupResult ResolveUiGetStringByIdForDiagnostics(int id)
	{
		EnsureLocaleLoaded();
		EnsureCodeStringMappingsLoaded();
		LocaleLookupResult localeLookupResult = ResolveLocaleTokenForDiagnostics(id);
		if (id <= 0)
		{
			return new LocaleLookupResult(resolved: false, "##" + id, "Ui.GetStringById refuses non-positive ids before CodeStringMappings lookup; " + _codeStringMappingSource, 0u, codeStringMappingExists: false, string.Empty, id, uiGetStringByIdAllowed: false);
		}
		if (_codeStringMappingsById == null || !_codeStringMappingsById.TryGetValue(id, out var value))
		{
			string source = (localeLookupResult.Resolved ? ("direct locale token exists, but Ui.GetStringById requires a CodeStringMappings id for this datasource; " + localeLookupResult.Source + " / " + _codeStringMappingSource) : ("missing CodeStringMappings id; " + _codeStringMappingSource));
			return new LocaleLookupResult(resolved: false, "##" + id, source, 0u, codeStringMappingExists: false, string.Empty, id);
		}
		if (KnownCodeStringLocaleLookupIdsById.TryGetValue(id, out var value2) && _localeStrings != null && _localeStrings.TryGetValue(value2, out var value3))
		{
			return new LocaleLookupResult(resolved: true, value3, "known CodeStringMappings locale lookup; " + _codeStringMappingSource + " -> " + _localeSource, value2, codeStringMappingExists: true, value, id);
		}
		if (TryParseLocaleKey(value, out var key))
		{
			return new LocaleLookupResult(resolved: false, "##" + id, "numeric CodeStringMappings aliases are not treated as locale hashes by the live client: " + value + "^" + id + "^. " + _codeStringMappingSource + " -> " + _localeSource, key, codeStringMappingExists: true, value, id);
		}
		return new LocaleLookupResult(resolved: false, "##" + id, "CodeStringMappings id exists but key did not resolve in locale: " + value + "; " + _codeStringMappingSource + " -> " + _localeSource, 0u, codeStringMappingExists: true, value, id);
	}

	internal static string ResolveClientResourceAssetForDiagnostics(string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
		{
			return "unresolved-empty-filename";
		}
		string[] candidateClientRoots = CandidateClientRoots;
		for (int i = 0; i < candidateClientRoots.Length; i++)
		{
			foreach (string clientResourceCandidatePath in GetClientResourceCandidatePaths(candidateClientRoots[i], filename))
			{
				if (File.Exists(clientResourceCandidatePath))
				{
					return "loose-resource:" + clientResourceCandidatePath;
				}
			}
		}
		return "missing-loose-resource:" + filename;
	}

	internal static bool ClientResourceAssetExistsForDiagnostics(string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
		{
			return false;
		}
		string[] candidateClientRoots = CandidateClientRoots;
		for (int i = 0; i < candidateClientRoots.Length; i++)
		{
			foreach (string clientResourceCandidatePath in GetClientResourceCandidatePaths(candidateClientRoots[i], filename))
			{
				if (File.Exists(clientResourceCandidatePath))
				{
					return true;
				}
			}
		}
		return false;
	}

	internal static string BuildPublicAssetUrlForDiagnostics(string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
		{
			return string.Empty;
		}
		return "http://assets.raisingkaines.com/assets/" + filename.Replace('\\', '/');
	}

	private static IEnumerable<string> GetClientResourceCandidatePaths(string root, string filename)
	{
		yield return Path.Combine(root, "Resources", filename);
		yield return Path.Combine(root, filename);
	}

	internal static void LogOuterGamesGridDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent)
	{
		LogBrowserV2AddGameBoundaryEvidence(playerGuid, worldTunnel, sourceEvent);
		string[] candidateClientRoots = CandidateClientRoots;
		foreach (string text in candidateClientRoots)
		{
			if (Directory.Exists(text))
			{
				LogOuterGamesGridDiagnosticsForRoot(playerGuid, worldTunnel, sourceEvent, text);
			}
		}
	}

	private static void LogOuterGamesGridDiagnosticsForRoot(ulong playerGuid, bool worldTunnel, string sourceEvent, string root)
	{
		Dictionary<int, string> imagesById = LoadImagesById(root);
		Dictionary<string, int> imageIdsByName = BuildImageIdsByName(imagesById);
		Dictionary<int, MiniGameDataDiagnosticRow> miniGameRows = LoadMiniGameDataDiagnosticRows(root);
		LogBrowserV2AddGameBoundaryRows(playerGuid, worldTunnel, sourceEvent, root, imageIdsByName);
		LogActivityCategoryDiagnostics(playerGuid, worldTunnel, sourceEvent, root, imageIdsByName);
		LogMiniGameTypeDiagnostics(playerGuid, worldTunnel, sourceEvent, root, imagesById);
		LogMiniGameGroupDataDiagnostics(playerGuid, worldTunnel, sourceEvent, root, imagesById);
		LogMiniGameGroupLinkDiagnostics(playerGuid, worldTunnel, sourceEvent, root, imagesById, miniGameRows);
		LogMiniGameDataStandaloneDiagnostics(playerGuid, worldTunnel, sourceEvent, root, imagesById, miniGameRows);
	}

	private static void LogActivityCategoryDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<string, int> imageIdsByName)
	{
		string path = Path.Combine(root, "Resources", "ActivityCategories.txt");
		if (!File.Exists(path))
		{
			return;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 5)
			{
				int rowId = ParseIntOrZero(array[0]);
				int groupId = ParseIntOrZero(array[1]);
				int nameId = ParseIntOrZero(array[2]);
				string text = array[4].Trim();
				int imageId = ResolveImageIdFromFilename(imageIdsByName, text);
				LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "ActivityCategories", rowId, 0, groupId, nameId, imageId, text, item);
			}
		}
	}

	private static void LogBrowserV2AddGameBoundaryEvidence(ulong playerGuid, bool worldTunnel, string sourceEvent)
	{
		MinigameDiagnosticsLog.Info("BrowserV2AddGameBoundaryEvidence", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("sourceHandler", "Browser_V2.PopulateGames"), MinigameDiagnosticsLog.Field("uiAsset", "UI\\Browser_V2.swf"), MinigameDiagnosticsLog.Field("uiModule", "UI\\UiModules\\Main\\wndBrowserV2.xml"), MinigameDiagnosticsLog.Field("decompiledScriptSource", "UI\\ScriptsBase.bin"), MinigameDiagnosticsLog.Field("dataSource", "GamesList_DS"), MinigameDiagnosticsLog.Field("nativeDataSource", "Activities.Ref.Categories"), MinigameDiagnosticsLog.Field("scriptEvidence", "BrowserV2.PopulateGames calls ds=GetDS('GamesList_DS'), then AddGame(GetData('NameId'), tonumber(GetData('Id')), GetData('DescriptionId'), tostring(GetData('ThumbnailImageFilename')), GetData('MysteryChestIcon'))."), MinigameDiagnosticsLog.Field("runningFreeRealmsRoots", GetRunningFreeRealmsRootsForDiagnostics()), MinigameDiagnosticsLog.Field("diagnosticScope", "final Browser_V2 AddGame boundary mirror; no opcode 167, MinigameDetail, locale, or gameplay changes"));
	}

	private static void LogBrowserV2AddGameBoundaryRows(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<string, int> imageIdsByName)
	{
		string path = Path.Combine(root, "Resources", "ActivityCategories.txt");
		if (!File.Exists(path))
		{
			return;
		}
		int num = 0;
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 5)
			{
				int num2 = ParseIntOrZero(array[0]);
				int num3 = ParseIntOrZero(array[1]);
				int num4 = ParseIntOrZero(array[2]);
				int num5 = ParseIntOrZero(array[3]);
				string text = array[4].Trim();
				string value = ((array.Length > 5) ? array[5].Trim() : string.Empty);
				int num6 = ResolveImageIdFromFilename(imageIdsByName, text);
				LocaleLookupResult localeLookupResult = ResolveUiGetStringByIdForDiagnostics(num4);
				LocaleLookupResult localeLookupResult2 = ResolveUiGetStringByIdForDiagnostics(num5);
				bool flag = string.Equals(localeLookupResult.Text, "Treasure War", StringComparison.OrdinalIgnoreCase) || string.Equals(localeLookupResult.Text, "Treasure Wars", StringComparison.OrdinalIgnoreCase) || string.Equals(localeLookupResult.Text, "Free Realms Treasure Wars", StringComparison.OrdinalIgnoreCase) || num4 == 1689170901 || num6 == 38530 || text.IndexOf("treasure_wars", StringComparison.OrdinalIgnoreCase) >= 0;
				MinigameDiagnosticsLog.Info("BrowserV2AddGameBoundaryRow", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("sourceHandler", "Browser_V2.PopulateGames"), MinigameDiagnosticsLog.Field("sourceDatasource", "GamesList_DS"), MinigameDiagnosticsLog.Field("nativeDatasource", "Activities.Ref.Categories"), MinigameDiagnosticsLog.Field("sourceTable", "ActivityCategories"), MinigameDiagnosticsLog.Field("clientRoot", root), MinigameDiagnosticsLog.Field("tileIndexZeroBased", num), MinigameDiagnosticsLog.Field("tileIndexOneBased", num + 1), MinigameDiagnosticsLog.Field("addGameCall", "AddGame(NameId, tonumber(Id), DescriptionId, tostring(ThumbnailImageFilename), MysteryChestIcon)"), MinigameDiagnosticsLog.Field("id", num2), MinigameDiagnosticsLog.Field("foundationGuid", string.Empty), MinigameDiagnosticsLog.Field("groupId", num3), MinigameDiagnosticsLog.Field("nameArgument", num4), MinigameDiagnosticsLog.Field("nameId", num4), MinigameDiagnosticsLog.Field("nameIdUnsigned", Unsigned(num4)), MinigameDiagnosticsLog.Field("resolvedName", localeLookupResult.Text), MinigameDiagnosticsLog.Field("resolvedNameSource", localeLookupResult.Source), MinigameDiagnosticsLog.Field("nameCodeStringMappingExists", localeLookupResult.CodeStringMappingExists), MinigameDiagnosticsLog.Field("nameCodeStringMappingKey", localeLookupResult.CodeStringMappingKey), MinigameDiagnosticsLog.Field("descriptionID", num5), MinigameDiagnosticsLog.Field("descriptionIdUnsigned", Unsigned(num5)), MinigameDiagnosticsLog.Field("resolvedDescription", localeLookupResult2.Text), MinigameDiagnosticsLog.Field("resolvedDescriptionSource", localeLookupResult2.Source), MinigameDiagnosticsLog.Field("descriptionCodeStringMappingExists", localeLookupResult2.CodeStringMappingExists), MinigameDiagnosticsLog.Field("descriptionCodeStringMappingKey", localeLookupResult2.CodeStringMappingKey), MinigameDiagnosticsLog.Field("imageFilename", text), MinigameDiagnosticsLog.Field("thumbnailImageFilename", text), MinigameDiagnosticsLog.Field("thumbnailImagePublicAssetUrl", BuildPublicAssetUrlForDiagnostics(text)), MinigameDiagnosticsLog.Field("thumbnailImageExists", ClientResourceAssetExistsForDiagnostics(text)), MinigameDiagnosticsLog.Field("imageId", num6), MinigameDiagnosticsLog.Field("mysteryChestIcon", value), MinigameDiagnosticsLog.Field("visibleTreasureWarTileCandidate", flag), MinigameDiagnosticsLog.Field("isTcgCategory", num2 == 11), MinigameDiagnosticsLog.Field("isLegacyTreasureWarCategory", num2 == 26), MinigameDiagnosticsLog.Field("rawLine", item));
				num++;
			}
		}
	}

	private static string GetRunningFreeRealmsRootsForDiagnostics()
	{
		try
		{
			List<string> list = new List<string>();
			Process[] processesByName = Process.GetProcessesByName("FreeRealms");
			foreach (Process process in processesByName)
			{
				try
				{
					string text = process.MainModule?.FileName ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add(Path.GetDirectoryName(text) ?? text);
					}
				}
				catch (Exception ex)
				{
					list.Add("pid=" + process.Id + ":unresolved:" + ex.GetType().Name);
				}
			}
			return (list.Count == 0) ? "no-running-FreeRealms.exe" : string.Join(" | ", list);
		}
		catch (Exception ex2)
		{
			return "process-scan-failed:" + ex2.GetType().Name;
		}
	}

	private static void LogMiniGameTypeDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<int, string> imagesById)
	{
		string path = Path.Combine(root, "Resources", "MiniGameTypeData.txt");
		if (!File.Exists(path))
		{
			return;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 10)
			{
				int num = ParseIntOrZero(array[0]);
				int nameId = ParseIntOrZero(array[1]);
				int imageId = ParseIntOrZero(array[9]);
				int groupId = ((array.Length > 12) ? ParseIntOrZero(array[12]) : 0);
				LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "MiniGameTypeData", num, num, groupId, nameId, imageId, ResolveImageFilename(imagesById, imageId), item);
			}
		}
	}

	private static void LogMiniGameGroupDataDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<int, string> imagesById)
	{
		string path = Path.Combine(root, "Resources", "MiniGameGroupData.txt");
		if (!File.Exists(path))
		{
			return;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 4)
			{
				int num = ParseIntOrZero(array[0]);
				int nameId = ParseIntOrZero(array[1]);
				int imageId = ParseIntOrZero(array[3]);
				int typeId = ((array.Length > 8) ? ParseIntOrZero(array[8]) : 0);
				LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "MiniGameGroupData", num, typeId, num, nameId, imageId, ResolveImageFilename(imagesById, imageId), item);
			}
		}
	}

	private static void LogMiniGameGroupLinkDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<int, string> imagesById, Dictionary<int, MiniGameDataDiagnosticRow> miniGameRows)
	{
		string path = Path.Combine(root, "Resources", "MiniGameGroupLinks.txt");
		if (!File.Exists(path))
		{
			return;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length < 2)
			{
				continue;
			}
			int num = ParseIntOrZero(array[0]);
			int num2 = ParseIntOrZero(array[1]);
			if (num2 == 1 || IsTcgOuterGridPollutionCandidate(num, 0, 0, 0, string.Empty))
			{
				if (!miniGameRows.TryGetValue(num, out var value))
				{
					LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "MiniGameGroupLinks(group=" + num2 + ")->missing-MiniGameData", num, 0, num2, 0, 0, string.Empty, item);
				}
				else
				{
					LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "MiniGameGroupLinks(group=" + num2 + ")->MiniGameData", value.RowId, value.TypeId, num2, value.NameId, value.ImageId, ResolveImageFilename(imagesById, value.ImageId), item + " | " + value.RawLine);
				}
			}
		}
	}

	private static void LogMiniGameDataStandaloneDiagnostics(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, Dictionary<int, string> imagesById, Dictionary<int, MiniGameDataDiagnosticRow> miniGameRows)
	{
		foreach (MiniGameDataDiagnosticRow value in miniGameRows.Values)
		{
			string imageFilename = ResolveImageFilename(imagesById, value.ImageId);
			if (value.TypeId == 11 || value.RowId == 727 || IsTcgOuterGridPollutionCandidate(value.RowId, value.TypeId, value.NameId, value.ImageId, imageFilename))
			{
				LogOuterGamesGridRowCandidate(playerGuid, worldTunnel, sourceEvent, root, "MiniGameData(type-or-tcg-suspect)", value.RowId, value.TypeId, 0, value.NameId, value.ImageId, imageFilename, value.RawLine);
			}
		}
	}

	private static void LogOuterGamesGridRowCandidate(ulong playerGuid, bool worldTunnel, string sourceEvent, string root, string sourceTable, int rowId, int typeId, int groupId, int nameId, int imageId, string imageFilename, string rawLine)
	{
		LocaleLookupResult localeLookupResult = ResolveUiGetStringByIdForDiagnostics(nameId);
		bool flag = IsTcgOuterGridPollutionCandidate(rowId, typeId, nameId, imageId, imageFilename);
		MinigameDiagnosticsLog.Info("OuterGamesGridRowCandidate", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("sourceHandler", "Browser_V2/static-resource-diagnostic"), MinigameDiagnosticsLog.Field("sourceTable", sourceTable), MinigameDiagnosticsLog.Field("clientRoot", root), MinigameDiagnosticsLog.Field("rowId", rowId), MinigameDiagnosticsLog.Field("typeId", typeId), MinigameDiagnosticsLog.Field("groupId", groupId), MinigameDiagnosticsLog.Field("nameId", nameId), MinigameDiagnosticsLog.Field("nameIdUnsigned", Unsigned(nameId)), MinigameDiagnosticsLog.Field("resolvedName", localeLookupResult.Text), MinigameDiagnosticsLog.Field("resolvedNameSource", localeLookupResult.Source), MinigameDiagnosticsLog.Field("nameCodeStringMappingExists", localeLookupResult.CodeStringMappingExists), MinigameDiagnosticsLog.Field("nameCodeStringMappingKey", localeLookupResult.CodeStringMappingKey), MinigameDiagnosticsLog.Field("imageId", imageId), MinigameDiagnosticsLog.Field("imageFilename", imageFilename), MinigameDiagnosticsLog.Field("imagePublicAssetUrl", BuildPublicAssetUrlForDiagnostics(imageFilename)), MinigameDiagnosticsLog.Field("imageExists", ClientResourceAssetExistsForDiagnostics(imageFilename)), MinigameDiagnosticsLog.Field("tcgOuterGridPollutionCandidate", flag), MinigameDiagnosticsLog.Field("treasureWarRow", rowId == 39), MinigameDiagnosticsLog.Field("nativeTcgRow", rowId == 727), MinigameDiagnosticsLog.Field("detailCarouselRow", rowId == 41 || rowId == 602 || rowId == 603 || rowId == 604), MinigameDiagnosticsLog.Field("suppressionScope", "diagnostic-only; do-not-remove-row-39-globally"), MinigameDiagnosticsLog.Field("rawLine", rawLine));
	}

	private static bool IsTcgOuterGridPollutionCandidate(int rowId, int typeId, int nameId, int imageId, string imageFilename)
	{
		if (rowId != 39 && rowId != 41 && rowId != 602 && rowId != 603 && rowId != 604 && rowId != 611 && rowId != 612 && rowId != 613 && rowId != 614 && rowId != 615 && typeId != 11 && nameId != 1689170901 && imageId != 38530)
		{
			if (!string.IsNullOrEmpty(imageFilename))
			{
				return imageFilename.IndexOf("treasure_wars", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			return false;
		}
		return true;
	}

	private static Dictionary<int, MiniGameDataDiagnosticRow> LoadMiniGameDataDiagnosticRows(string root)
	{
		Dictionary<int, MiniGameDataDiagnosticRow> dictionary = new Dictionary<int, MiniGameDataDiagnosticRow>();
		string path = Path.Combine(root, "Resources", "MiniGameData.txt");
		if (!File.Exists(path))
		{
			return dictionary;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 5)
			{
				int num = ParseIntOrZero(array[0]);
				if (!dictionary.ContainsKey(num))
				{
					dictionary.Add(num, new MiniGameDataDiagnosticRow(num, ParseIntOrZero(array[1]), ParseIntOrZero(array[2]), ParseIntOrZero(array[3]), ParseIntOrZero(array[4]), item));
				}
			}
		}
		return dictionary;
	}

	private static Dictionary<int, string> LoadImagesById(string root)
	{
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		string path = Path.Combine(root, "Resources", "Images", "Images.txt");
		if (!File.Exists(path))
		{
			return dictionary;
		}
		foreach (string item in File.ReadLines(path))
		{
			string[] array = SplitResourceLine(item);
			if (array.Length >= 2)
			{
				int key = ParseIntOrZero(array[0]);
				if (!dictionary.ContainsKey(key))
				{
					dictionary.Add(key, array[1].Trim());
				}
			}
		}
		return dictionary;
	}

	private static Dictionary<string, int> BuildImageIdsByName(Dictionary<int, string> imagesById)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<int, string> item in imagesById)
		{
			if (!string.IsNullOrWhiteSpace(item.Value) && !dictionary.ContainsKey(item.Value))
			{
				dictionary.Add(item.Value, item.Key);
			}
		}
		return dictionary;
	}

	private static int ResolveImageIdFromFilename(Dictionary<string, int> imageIdsByName, string filename)
	{
		if (string.IsNullOrWhiteSpace(filename))
		{
			return 0;
		}
		if (!imageIdsByName.TryGetValue(filename, out var value))
		{
			return 0;
		}
		return value;
	}

	private static string ResolveImageFilename(Dictionary<int, string> imagesById, int imageId)
	{
		if (!imagesById.TryGetValue(imageId, out var value))
		{
			return string.Empty;
		}
		return value;
	}

	private static string[] SplitResourceLine(string line)
	{
		return (line ?? string.Empty).Split(new char[1] { '^' });
	}

	private static int ParseIntOrZero(string value)
	{
		if (!int.TryParse((value ?? string.Empty).Trim(), out var result))
		{
			return 0;
		}
		return result;
	}

	private static void EnsureLocaleLoaded()
	{
		if (_localeStrings != null)
		{
			return;
		}
		lock (LocaleLock)
		{
			if (_localeStrings == null)
			{
				_localeStrings = LoadLocaleStrings(out _localeSource);
			}
		}
	}

	private static void EnsureCodeStringMappingsLoaded()
	{
		if (_codeStringMappingsById != null)
		{
			return;
		}
		lock (CodeStringMappingLock)
		{
			if (_codeStringMappingsById == null)
			{
				_codeStringMappingsById = LoadCodeStringMappings(out _codeStringMappingSource);
			}
		}
	}

	private static Dictionary<uint, string> LoadLocaleStrings(out string source)
	{
		source = "missing:locale/en_us_data.dat";
		Dictionary<uint, string> dictionary = new Dictionary<uint, string>();
		string[] candidateClientRoots = CandidateClientRoots;
		for (int i = 0; i < candidateClientRoots.Length; i++)
		{
			string text = Path.Combine(candidateClientRoots[i], "locale", "en_us_data.dat");
			if (!File.Exists(text))
			{
				continue;
			}
			source = text;
			{
				foreach (string item in File.ReadLines(text))
				{
					if (!string.IsNullOrWhiteSpace(item))
					{
						string[] array = item.Split(new char[1] { '\t' }, 3);
						if (array.Length >= 3 && TryParseLocaleKey(array[0], out var key) && !dictionary.ContainsKey(key))
						{
							dictionary.Add(key, array[2]);
						}
					}
				}
				return dictionary;
			}
		}
		return dictionary;
	}

	private static Dictionary<int, string> LoadCodeStringMappings(out string source)
	{
		source = "missing:Resources/CodeStringMappings.txt";
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		string[] candidateClientRoots = CandidateClientRoots;
		for (int i = 0; i < candidateClientRoots.Length; i++)
		{
			string text = Path.Combine(candidateClientRoots[i], "Resources", "CodeStringMappings.txt");
			if (!File.Exists(text))
			{
				continue;
			}
			source = text;
			{
				foreach (string item in File.ReadLines(text))
				{
					if (string.IsNullOrWhiteSpace(item) || item[0] == '#')
					{
						continue;
					}
					string[] array = item.Split(new char[1] { '^' }, StringSplitOptions.RemoveEmptyEntries);
					if (array.Length >= 2)
					{
						string value = array[0].Trim();
						if (int.TryParse(array[1].Trim(), out var result) && !dictionary.ContainsKey(result))
						{
							dictionary.Add(result, value);
						}
					}
				}
				return dictionary;
			}
		}
		return dictionary;
	}

	private static bool TryParseLocaleKey(string value, out uint key)
	{
		if (uint.TryParse(value, out key))
		{
			return true;
		}
		if (int.TryParse(value, out var result))
		{
			key = (uint)result;
			return true;
		}
		key = 0u;
		return false;
	}
}
