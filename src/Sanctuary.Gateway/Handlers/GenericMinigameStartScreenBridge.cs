using System;
using System.Collections.Generic;
using System.IO;
using Sanctuary.Core.IO;

namespace Sanctuary.Gateway.Handlers;

internal static class GenericMinigameStartScreenBridge
{
	private sealed class MiniGameDataRow
	{
		public readonly int Id;

		public readonly int TypeId;

		public readonly int NameId;

		public readonly int DescriptionId;

		public readonly int ImageId;

		public readonly int Difficulty;

		public MiniGameDataRow(int id, int typeId, int nameId, int descriptionId, int imageId, int difficulty)
		{
			Id = id;
			TypeId = typeId;
			NameId = nameId;
			DescriptionId = descriptionId;
			ImageId = imageId;
			Difficulty = difficulty;
		}
	}

	private const uint MinigameActivityLaunchProcessorHash = 526123817u;

	private const int GenericMiniGameGroupId = 69;

	private const int GenericLaunchRequestId = 69420;

	private const string ClientFlashBackgroundSwf = "game_hidden.gfx";

	private static readonly object MiniGameDataLock = new object();

	private static Dictionary<int, MiniGameDataRow> _miniGameDataRows;

	private static string _miniGameDataSource = string.Empty;

	public static bool TryHandleJoinActivityRequest(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int requestedActivityId, ReadOnlySpan<byte> remaining)
	{
		if (requestedActivityId > 0 && TcgSessionRegistry.IsTcgLaunchActivityId(requestedActivityId))
		{
			return false;
		}
		PacketReadyActivityDefinitionRow row = null;
		bool flag = requestedActivityId > 0 && PacketReadyActivityDefinitions.TryGetRowByActivityId(requestedActivityId, playerGuid, worldTunnel, out row);
		string selectionSource = (flag ? "activity-service-request" : "unresolved");
		bool usedRecentSelection = false;
		double selectionAgeMs = -1.0;
		int requestedCategoryId = 0;
		int activityCategoryId = 0;
		int selectedRowId = 0;
		if (WallOfDataUIEventPacketHandler.TryGetRecentGenericMinigameSelectionForPlay(playerGuid, out selectionAgeMs, out requestedCategoryId, out activityCategoryId, out selectedRowId) && selectedRowId > 0 && (!flag || requestedActivityId <= 0 || requestedActivityId == requestedCategoryId || requestedActivityId == activityCategoryId) && PacketReadyActivityDefinitions.TryGetRowByActivityId(selectedRowId, playerGuid, worldTunnel, out var row2))
		{
			row = row2;
			selectionSource = "recent-generic-detail-selection";
			usedRecentSelection = true;
		}
		if (row == null)
		{
			return false;
		}
		if (row.Id == 7 || TcgSessionRegistry.IsTcgLaunchActivityId(row.Id))
		{
			return false;
		}
		string sourcePath;
		MiniGameDataRow miniGameDataRow = (TryGetMiniGameDataRow(row.Unknown3, out sourcePath) ? _miniGameDataRows[row.Unknown3] : null);
		int id = row.Id;
		int nameId = ((row.DisplayNameId != 0) ? row.DisplayNameId : (miniGameDataRow?.NameId ?? row.NameId));
		int descriptionId = ((row.DisplayDescriptionId != 0) ? row.DisplayDescriptionId : (miniGameDataRow?.DescriptionId ?? row.DescriptionId));
		int iconId = ((row.ImageSetId != 0) ? row.ImageSetId : (miniGameDataRow?.ImageId ?? 0));
		int difficulty = ((row.Difficulty != 0) ? row.Difficulty : (miniGameDataRow?.Difficulty ?? 1));
		string source;
		int num = ResolveMiniGameType(id, row, miniGameDataRow, out source);
		if (IsTradingCardActivity(row, miniGameDataRow, num))
		{
			MinigameDiagnosticsLog.Info("GenericMinigameStartScreenSkipped", "GenericMinigameStartScreenBridge", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", "tcg-row-preserved-for-native-handler"), MinigameDiagnosticsLog.Field("requestedActivity", requestedActivityId), MinigameDiagnosticsLog.Field("packetReadyRowId", row.Id), MinigameDiagnosticsLog.Field("backingMiniGameDataId", row.Unknown3), MinigameDiagnosticsLog.Field("categoryId", row.Category), MinigameDiagnosticsLog.Field("miniGameType", num), MinigameDiagnosticsLog.Field("miniGameDataType", miniGameDataRow?.TypeId ?? 0), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.HexField("remaining", remaining));
			return false;
		}
		bool flag2 = IsClientFlashStyle(id, row, num);
		string backgroundSwf = (flag2 ? "game_hidden.gfx" : string.Empty);
		int profileType = ((num != 16) ? 1 : 0);
		byte[] sysSpecificData = BuildSysSpecificData(nameId, descriptionId, iconId, difficulty, profileType, num, id, flag2, backgroundSwf);
		byte[] array = BuildInviteDetails(playerGuid, id, nameId, descriptionId, iconId, sysSpecificData);
		byte[] array2 = BuildActivityLaunched(id, playerGuid);
		byte[] array3 = BuildMiniGameInfoPacket(id, nameId, descriptionId, iconId, difficulty, profileType, num, id, 0, flag2, backgroundSwf);
		TcgTunnelSend.Send(connection, worldTunnel: false, array);
		TcgTunnelSend.Send(connection, worldTunnel: false, array2);
		TcgTunnelSend.Send(connection, worldTunnel: false, array3);
		MinigameDiagnosticsLog.Info("ActivityInviteSent", "GenericMinigameStartScreenBridge", playerGuid, worldTunnel: false, BuildLogFields(requestedActivityId, row, id, nameId, descriptionId, iconId, difficulty, num, source, flag2, backgroundSwf, miniGameDataRow, sourcePath, array.Length, array2.Length, array3.Length, didSendInvite: true, selectionSource, usedRecentSelection, selectionAgeMs, requestedCategoryId, activityCategoryId, selectedRowId, remaining));
		MinigameDiagnosticsLog.Info("GenericMinigameStartScreenPacketsSent", "GenericMinigameStartScreenBridge", playerGuid, worldTunnel: false, BuildLogFields(requestedActivityId, row, id, nameId, descriptionId, iconId, difficulty, num, source, flag2, backgroundSwf, miniGameDataRow, sourcePath, array.Length, array2.Length, array3.Length, didSendInvite: true, selectionSource, usedRecentSelection, selectionAgeMs, requestedCategoryId, activityCategoryId, selectedRowId, remaining));
		return true;
	}

	public static void SendTradingCardStartScreenActivityLaunchPrelude(GatewayConnection connection, bool sourceWorldTunnel, ulong playerGuid, int launchActivityId, int selectedRowId, int displayRowId, int nameId, int descriptionId, int iconId, int difficulty, string selectedTitle, string selectedDescription, ReadOnlySpan<byte> remaining)
	{
		int num = ((difficulty <= 0) ? 1 : difficulty);
		PacketReadyActivityDefinitionRow row;
		bool packetReadyRowResolved = PacketReadyActivityDefinitions.TryGetRowByActivityId(launchActivityId, playerGuid, sourceWorldTunnel, out row);
		string tcgBackgroundSwf = "game_hidden.gfx";
		byte[] sysSpecificData = BuildSysSpecificData(nameId, descriptionId, iconId, num, 0, 16, launchActivityId, clientFlashStyle: false, tcgBackgroundSwf);
		byte[] array = BuildInviteDetails(playerGuid, launchActivityId, nameId, descriptionId, iconId, sysSpecificData);
		byte[] array2 = BuildActivityLaunched(launchActivityId, playerGuid);
		byte[] array3 = BuildMiniGameInfoPacket(launchActivityId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, clientFlashStyle: false, tcgBackgroundSwf);
		TcgTunnelSend.Send(connection, worldTunnel: false, array);
		LogTcgMiniGameInfoUnknown13Sent(playerGuid, sourceWorldTunnel, "InviteDetails sys-specific", launchActivityId, selectedRowId, displayRowId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, row?.Category ?? 11, tcgBackgroundSwf, sysSpecificData);
		LogTcgStartScreenVisualStatePacket(playerGuid, sourceWorldTunnel, "ClientActivityLaunch:InviteDetails", launchActivityId, selectedRowId, displayRowId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, packetReadyRowResolved, row, clientFlashStyle: false, miniGameInfoShowStartScreenGate: true, tcgBackgroundSwf, array.Length, sentFinalReadyPacket: false, selectedTitle, selectedDescription, remaining);
		TcgTunnelSend.Send(connection, worldTunnel: false, array2);
		LogTcgStartScreenVisualStatePacket(playerGuid, sourceWorldTunnel, "ClientActivityLaunch:ActivityLaunched", launchActivityId, selectedRowId, displayRowId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, packetReadyRowResolved, row, clientFlashStyle: false, miniGameInfoShowStartScreenGate: true, tcgBackgroundSwf, array2.Length, sentFinalReadyPacket: false, selectedTitle, selectedDescription, remaining);
		TcgTunnelSend.Send(connection, worldTunnel: false, array3);
		LogTcgMiniGameInfoUnknown13Sent(playerGuid, sourceWorldTunnel, "standalone MiniGameInfoPacket", launchActivityId, selectedRowId, displayRowId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, row?.Category ?? 11, tcgBackgroundSwf, array3);
		LogTcgStartScreenVisualStatePacket(playerGuid, sourceWorldTunnel, "MiniGameInfoPacket", launchActivityId, selectedRowId, displayRowId, nameId, descriptionId, iconId, num, 0, 16, launchActivityId, 0, packetReadyRowResolved, row, clientFlashStyle: false, miniGameInfoShowStartScreenGate: true, tcgBackgroundSwf, array3.Length, sentFinalReadyPacket: false, selectedTitle, selectedDescription, remaining);
		MinigameDiagnosticsLog.Info("TcgStartScreenActivityLaunchPreludeSent", "GenericMinigameStartScreenBridge", playerGuid, false, MinigameDiagnosticsLog.Field("sourceWorldTunnel", sourceWorldTunnel), MinigameDiagnosticsLog.Field("launchActivityId", launchActivityId), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("displayRowId", displayRowId), MinigameDiagnosticsLog.Field("selectedTitle", selectedTitle), MinigameDiagnosticsLog.Field("selectedDescription", selectedDescription), MinigameDiagnosticsLog.Field("nameId", nameId), MinigameDiagnosticsLog.Field("descriptionId", descriptionId), MinigameDiagnosticsLog.Field("iconId", iconId), MinigameDiagnosticsLog.Field("difficulty", num), MinigameDiagnosticsLog.Field("profileType", 0), MinigameDiagnosticsLog.Field("miniGameType", 16), MinigameDiagnosticsLog.Field("preselectedGameId", launchActivityId), MinigameDiagnosticsLog.Field("preselectedGameIdSource", "launchActivityId"), MinigameDiagnosticsLog.Field("miniGameInfoUnknown13", tcgBackgroundSwf), MinigameDiagnosticsLog.Field("backgroundSwf", tcgBackgroundSwf), MinigameDiagnosticsLog.Field("miniGameInfoUnknown20", 0), MinigameDiagnosticsLog.Field("miniGameInfoColumn16", 0), MinigameDiagnosticsLog.Field("categoryId", row?.Category ?? 11), MinigameDiagnosticsLog.Field("canPlayerJoin", row?.PlayerCanJoin ?? true), MinigameDiagnosticsLog.Field("membersOnly", row?.MembersOnly ?? false), MinigameDiagnosticsLog.Field("price", 0), MinigameDiagnosticsLog.Field("buyRequired", false), MinigameDiagnosticsLog.Field("locked", false), MinigameDiagnosticsLog.Field("buttonModeKnown", false), MinigameDiagnosticsLog.Field("buttonModeServerAssumption", "Sanctuary activity-7 MiniGameInfo baseline with native type-16 Unknown13 asset gate satisfied by game_hidden.gfx"), MinigameDiagnosticsLog.Field("comparisonBaseline", "Sanctuary source writes PreselectedGameId=7; native FUN_009beb70 requires Unknown13 length > 0 for type 16 to advance to asset/controller prep."), MinigameDiagnosticsLog.Field("sentPackets", "ClientActivityLaunch:InviteDetails,ClientActivityLaunch:ActivityLaunched,MiniGameInfoPacket"), MinigameDiagnosticsLog.Field("sentFinalReadyPacket", false), MinigameDiagnosticsLog.Field("sanctuaryParity", "activity-7 invite/launched/minigame-info prelude preserved except proven Unknown13 precondition"), MinigameDiagnosticsLog.Field("inviteDetailsBytes", array.Length), MinigameDiagnosticsLog.Field("activityLaunchedBytes", array2.Length), MinigameDiagnosticsLog.Field("miniGameInfoBytes", array3.Length), MinigameDiagnosticsLog.Field("usesPacketReadyActivity7", launchActivityId == 7 || (row != null && row.Id == 7)), MinigameDiagnosticsLog.Field("usesLegacyRow41", launchActivityId == 41 || displayRowId == 41), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", true), MinigameDiagnosticsLog.HexField("remaining", remaining));
	}

	private static void LogTcgStartScreenVisualStatePacket(ulong playerGuid, bool sourceWorldTunnel, string packetName, int launchActivityId, int selectedRowId, int displayRowId, int nameId, int descriptionId, int iconId, int difficulty, int profileType, int miniGameType, int preselectedGameId, int miniGameInfoUnknown20, bool packetReadyRowResolved, PacketReadyActivityDefinitionRow packetReadyRow, bool clientFlashStyle, bool miniGameInfoShowStartScreenGate, string backgroundSwf, int packetBytes, bool sentFinalReadyPacket, string selectedTitle, string selectedDescription, ReadOnlySpan<byte> remaining)
	{
		string value = ((preselectedGameId == 387) ? "nativeTcgMiniGameDataId" : ((preselectedGameId == displayRowId) ? "displayRowId" : "launchActivityId"));
		MinigameDiagnosticsLog.Info("TcgStartScreenVisualStatePacket", "GenericMinigameStartScreenBridge", playerGuid, false, MinigameDiagnosticsLog.Field("packetName", packetName), MinigameDiagnosticsLog.Field("sourceWorldTunnel", sourceWorldTunnel), MinigameDiagnosticsLog.Field("activityId", launchActivityId), MinigameDiagnosticsLog.Field("packetReadyRowResolved", packetReadyRowResolved), MinigameDiagnosticsLog.Field("packetReadyRowId", packetReadyRow?.Id ?? 0), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("displayRowId", displayRowId), MinigameDiagnosticsLog.Field("categoryId", packetReadyRow?.Category ?? 11), MinigameDiagnosticsLog.Field("typeId", miniGameType), MinigameDiagnosticsLog.Field("profileType", profileType), MinigameDiagnosticsLog.Field("canPlayerJoin", packetReadyRow?.PlayerCanJoin ?? true), MinigameDiagnosticsLog.Field("membersOnly", packetReadyRow?.MembersOnly ?? false), MinigameDiagnosticsLog.Field("price", 0), MinigameDiagnosticsLog.Field("buyRequired", false), MinigameDiagnosticsLog.Field("locked", false), MinigameDiagnosticsLog.Field("entitlementState", "playable-assumed"), MinigameDiagnosticsLog.Field("buttonModeKnown", false), MinigameDiagnosticsLog.Field("buttonModeInput", sentFinalReadyPacket ? "ready/actionable" : "pre-ready/loading"), MinigameDiagnosticsLog.Field("titleId", nameId), MinigameDiagnosticsLog.Field("descriptionId", descriptionId), MinigameDiagnosticsLog.Field("selectedTitle", selectedTitle), MinigameDiagnosticsLog.Field("selectedDescription", selectedDescription), MinigameDiagnosticsLog.Field("iconId", iconId), MinigameDiagnosticsLog.Field("difficulty", difficulty), MinigameDiagnosticsLog.Field("targetsTcg", true), MinigameDiagnosticsLog.Field("usesPacketReadyActivity7", launchActivityId == 7 || (packetReadyRow != null && packetReadyRow.Id == 7)), MinigameDiagnosticsLog.Field("usesLegacyRow41", launchActivityId == 41 || displayRowId == 41), MinigameDiagnosticsLog.Field("preselectedGameId", preselectedGameId), MinigameDiagnosticsLog.Field("preselectedGameIdSource", value), MinigameDiagnosticsLog.Field("miniGameInfoUnknown13", backgroundSwf), MinigameDiagnosticsLog.Field("backgroundSwf", backgroundSwf), MinigameDiagnosticsLog.Field("miniGameInfoUnknown20", miniGameInfoUnknown20), MinigameDiagnosticsLog.Field("miniGameInfoColumn16", miniGameInfoUnknown20), MinigameDiagnosticsLog.Field("clientFlashStyle", clientFlashStyle), MinigameDiagnosticsLog.Field("miniGameInfoUnknown11_showStartScreenGate", miniGameInfoShowStartScreenGate), MinigameDiagnosticsLog.Field("sentFinalReadyPacket", sentFinalReadyPacket), MinigameDiagnosticsLog.Field("packetBytes", packetBytes), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.HexField("remaining", remaining));
	}

	private static void LogTcgMiniGameInfoUnknown13Sent(ulong playerGuid, bool sourceWorldTunnel, string context, int launchActivityId, int selectedRowId, int displayRowId, int nameId, int descriptionId, int iconId, int difficulty, int profileType, int miniGameType, int preselectedGameId, int miniGameInfoUnknown20, int categoryId, string backgroundSwf, ReadOnlySpan<byte> payload)
	{
		MinigameDiagnosticsLog.Info("TcgMiniGameInfoUnknown13Sent", "GenericMinigameStartScreenBridge", playerGuid, false, MinigameDiagnosticsLog.Field("context", context), MinigameDiagnosticsLog.Field("sourceWorldTunnel", sourceWorldTunnel), MinigameDiagnosticsLog.Field("activityId", launchActivityId), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("selectedRowId", selectedRowId), MinigameDiagnosticsLog.Field("displayRowId", displayRowId), MinigameDiagnosticsLog.Field("typeId", miniGameType), MinigameDiagnosticsLog.Field("categoryId", categoryId), MinigameDiagnosticsLog.Field("nameId", nameId), MinigameDiagnosticsLog.Field("descriptionId", descriptionId), MinigameDiagnosticsLog.Field("iconId", iconId), MinigameDiagnosticsLog.Field("difficulty", difficulty), MinigameDiagnosticsLog.Field("profileType", profileType), MinigameDiagnosticsLog.Field("preselectedGameId", preselectedGameId), MinigameDiagnosticsLog.Field("miniGameInfoUnknown13", backgroundSwf), MinigameDiagnosticsLog.Field("backgroundSwf", backgroundSwf), MinigameDiagnosticsLog.Field("miniGameInfoUnknown20", miniGameInfoUnknown20), MinigameDiagnosticsLog.Field("isTcgType16", miniGameType == 16), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.HexField("payload", payload));
	}

	private static string[] BuildLogFields(int requestedActivityId, PacketReadyActivityDefinitionRow activityRow, int launchActivityId, int nameId, int descriptionId, int iconId, int difficulty, int miniGameType, string miniGameTypeSource, bool clientFlashStyle, string backgroundSwf, MiniGameDataRow miniGameData, string miniGameDataSource, int inviteBytes, int launchedBytes, int miniGameInfoBytes, bool didSendInvite, string selectionSource, bool usedRecentSelection, double recentSelectionAgeMs, int recentRequestedCategoryId, int recentActivityCategoryId, int recentSelectedRowId, ReadOnlySpan<byte> remaining)
	{
		return new string[36]
		{
			MinigameDiagnosticsLog.Field("requestedActivity", requestedActivityId),
			MinigameDiagnosticsLog.Field("selectionSource", selectionSource),
			MinigameDiagnosticsLog.Field("usedRecentGenericSelection", usedRecentSelection),
			MinigameDiagnosticsLog.Field("recentSelectionAgeMs", recentSelectionAgeMs),
			MinigameDiagnosticsLog.Field("recentRequestedCategoryId", recentRequestedCategoryId),
			MinigameDiagnosticsLog.Field("recentActivityCategoryId", recentActivityCategoryId),
			MinigameDiagnosticsLog.Field("recentSelectedRowId", recentSelectedRowId),
			MinigameDiagnosticsLog.Field("selectedGame", "NameId:" + nameId),
			MinigameDiagnosticsLog.Field("selectedId", launchActivityId),
			MinigameDiagnosticsLog.Field("activityId", launchActivityId),
			MinigameDiagnosticsLog.Field("packetReadyRowId", activityRow.Id),
			MinigameDiagnosticsLog.Field("backingMiniGameDataId", activityRow.Unknown3),
			MinigameDiagnosticsLog.Field("categoryId", activityRow.Category),
			MinigameDiagnosticsLog.Field("miniGameId", activityRow.Unknown3),
			MinigameDiagnosticsLog.Field("miniGameType", miniGameType),
			MinigameDiagnosticsLog.Field("miniGameTypeSource", miniGameTypeSource),
			MinigameDiagnosticsLog.Field("profileType", (miniGameType != 16) ? 1 : 0),
			MinigameDiagnosticsLog.Field("nameId", nameId),
			MinigameDiagnosticsLog.Field("descriptionId", descriptionId),
			MinigameDiagnosticsLog.Field("iconId", iconId),
			MinigameDiagnosticsLog.Field("difficulty", difficulty),
			MinigameDiagnosticsLog.Field("clientFlashStyle", clientFlashStyle),
			MinigameDiagnosticsLog.Field("backgroundSwf", backgroundSwf),
			MinigameDiagnosticsLog.Field("miniGameDataSource", miniGameDataSource),
			MinigameDiagnosticsLog.Field("miniGameDataFound", miniGameData != null),
			MinigameDiagnosticsLog.Field("miniGameDataType", miniGameData?.TypeId ?? 0),
			MinigameDiagnosticsLog.Field("handler", "GenericMinigameStartScreenBridge"),
			MinigameDiagnosticsLog.Field("sourceEvent", "ActivityServiceJoinActivityRequest"),
			MinigameDiagnosticsLog.Field("didLaunch", false),
			MinigameDiagnosticsLog.Field("didSendInvite", didSendInvite),
			MinigameDiagnosticsLog.Field("didShowNoGameSelected", false),
			MinigameDiagnosticsLog.Field("sentPackets", "ClientActivityLaunch:InviteDetails,ClientActivityLaunch:ActivityLaunched,MiniGameInfoPacket"),
			MinigameDiagnosticsLog.Field("inviteDetailsBytes", inviteBytes),
			MinigameDiagnosticsLog.Field("activityLaunchedBytes", launchedBytes),
			MinigameDiagnosticsLog.Field("miniGameInfoBytes", miniGameInfoBytes),
			MinigameDiagnosticsLog.HexField("remaining", remaining)
		};
	}

	private static byte[] BuildSysSpecificData(int nameId, int descriptionId, int iconId, int difficulty, int profileType, int miniGameType, int preselectedGameId, bool clientFlashStyle, string backgroundSwf, bool showStartScreenGate = true, int miniGameInfoUnknown20 = 0)
	{
		using PacketWriter packetWriter = new PacketWriter();
		WriteMiniGameInfo(packetWriter, nameId, descriptionId, iconId, difficulty, profileType, miniGameType, preselectedGameId, showStartScreenGate, backgroundSwf, miniGameInfoUnknown20);
		packetWriter.Write(clientFlashStyle ? 1 : 0);
		packetWriter.Write(clientFlashStyle ? 2 : 0);
		packetWriter.Write(BuildMiniGameGroupInfo(nameId, descriptionId, iconId, clientFlashStyle ? backgroundSwf : string.Empty));
		return packetWriter.Buffer;
	}

	private static byte[] BuildInviteDetails(ulong playerGuid, int activityId, int nameId, int descriptionId, int iconId, byte[] sysSpecificData)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)175);
		packetWriter.Write(1);
		packetWriter.Write(activityId);
		packetWriter.Write(0);
		packetWriter.Write(value: false);
		packetWriter.Write(playerGuid);
		packetWriter.Write("Test");
		packetWriter.Write(0);
		packetWriter.Write(1);
		packetWriter.Write(1);
		packetWriter.Write(playerGuid);
		packetWriter.Write(string.Empty);
		packetWriter.Write((byte)2);
		packetWriter.Write(value: true);
		WriteActivityLaunchRequest(packetWriter, playerGuid, nameId, descriptionId, iconId, sysSpecificData);
		return packetWriter.Buffer;
	}

	private static byte[] BuildActivityLaunched(int activityId, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)175);
		packetWriter.Write(5);
		packetWriter.Write(activityId);
		packetWriter.Write(0);
		packetWriter.Write(1);
		packetWriter.Write(playerGuid);
		return packetWriter.Buffer;
	}

	private static byte[] BuildMiniGameInfoPacket(int activityId, int nameId, int descriptionId, int iconId, int difficulty, int profileType, int miniGameType, int preselectedGameId, int miniGameInfoUnknown20, bool clientFlashStyle, string backgroundSwf, bool showStartScreenGate = true)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)39);
		packetWriter.Write((byte)16);
		packetWriter.Write(activityId);
		packetWriter.Write(-1);
		packetWriter.Write(-1);
		WriteMiniGameInfo(packetWriter, nameId, descriptionId, iconId, difficulty, profileType, miniGameType, preselectedGameId, showStartScreenGate, backgroundSwf, miniGameInfoUnknown20);
		packetWriter.Write(0);
		return packetWriter.Buffer;
	}

	private static void WriteActivityLaunchRequest(PacketWriter writer, ulong playerGuid, int nameId, int descriptionId, int iconId, byte[] sysSpecificData)
	{
		writer.Write(playerGuid);
		writer.Write(526123817u);
		writer.Write(0);
		writer.Write(value: false);
		writer.Write(69420);
		writer.Write(1);
		writer.Write(1);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(iconId);
		writer.Write(nameId);
		writer.Write(descriptionId);
		writer.WritePayload(sysSpecificData);
	}

	private static byte[] BuildMiniGameGroupInfo(int nameId, int descriptionId, int iconId, string backgroundSwf)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write(69);
		packetWriter.Write(nameId);
		packetWriter.Write(descriptionId);
		packetWriter.Write(iconId);
		packetWriter.Write(backgroundSwf);
		packetWriter.Write(0);
		packetWriter.Write(0);
		packetWriter.Write(string.Empty);
		packetWriter.Write(value: false);
		packetWriter.Write(0);
		return packetWriter.Buffer;
	}

	private static void WriteMiniGameInfo(PacketWriter writer, int nameId, int descriptionId, int iconId, int difficulty, int profileType, int miniGameType, int preselectedGameId, bool showStartScreenGate, string backgroundSwf, int miniGameInfoUnknown20 = 0)
	{
		writer.Write(nameId);
		writer.Write(iconId);
		writer.Write(descriptionId);
		writer.Write(difficulty);
		writer.Write(profileType);
		writer.Write(miniGameType);
		writer.Write(value: false);
		WriteEmptyRewardBundle(writer);
		WriteEmptyRewardBundle(writer);
		WriteEmptyRewardBundle(writer);
		writer.Write(0);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(showStartScreenGate);
		writer.Write(value: false);
		writer.Write(backgroundSwf);
		writer.Write(0);
		writer.Write(value: false);
		writer.Write(preselectedGameId);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(value: false);
		writer.Write(miniGameInfoUnknown20);
	}

	private static void WriteEmptyRewardBundle(PacketWriter writer)
	{
		writer.Write(value: false);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0L);
		writer.Write(0L);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
	}

	private static int ResolveMiniGameType(int requestedActivityId, PacketReadyActivityDefinitionRow activityRow, MiniGameDataRow miniGameData, out string source)
	{
		if (requestedActivityId == 1113 || activityRow.Id == 1113)
		{
			source = "sanctuary-minigame-mining-practice-client-flash";
			return 3;
		}
		if (miniGameData != null && miniGameData.TypeId != 0)
		{
			source = "MiniGameData.Unknown3";
			return miniGameData.TypeId;
		}
		source = "packet-ready-category-fallback";
		return activityRow.Category;
	}

	private static bool IsTradingCardActivity(PacketReadyActivityDefinitionRow activityRow, MiniGameDataRow miniGameData, int miniGameType)
	{
		if (activityRow.Category != 11 && miniGameType != 16)
		{
			if (miniGameData == null)
			{
				return false;
			}
			return miniGameData.TypeId == 16;
		}
		return true;
	}

	private static bool IsClientFlashStyle(int requestedActivityId, PacketReadyActivityDefinitionRow activityRow, int miniGameType)
	{
		if (requestedActivityId != 1113 && activityRow.Id != 1113)
		{
			return miniGameType == 3;
		}
		return true;
	}

	private static bool TryGetMiniGameDataRow(int miniGameDataId, out string sourcePath)
	{
		sourcePath = string.Empty;
		if (miniGameDataId <= 0)
		{
			return false;
		}
		EnsureMiniGameDataLoaded();
		sourcePath = _miniGameDataSource;
		if (_miniGameDataRows != null)
		{
			return _miniGameDataRows.ContainsKey(miniGameDataId);
		}
		return false;
	}

	private static void EnsureMiniGameDataLoaded()
	{
		if (_miniGameDataRows != null)
		{
			return;
		}
		lock (MiniGameDataLock)
		{
			if (_miniGameDataRows != null)
			{
				return;
			}
			Dictionary<int, MiniGameDataRow> dictionary = new Dictionary<int, MiniGameDataRow>();
			foreach (string miniGameDataCandidatePath in GetMiniGameDataCandidatePaths())
			{
				if (!File.Exists(miniGameDataCandidatePath))
				{
					continue;
				}
				foreach (string item in File.ReadLines(miniGameDataCandidatePath))
				{
					if (TryParseMiniGameDataRow(item, out var row))
					{
						dictionary[row.Id] = row;
					}
				}
				_miniGameDataRows = dictionary;
				_miniGameDataSource = miniGameDataCandidatePath;
				return;
			}
			_miniGameDataRows = dictionary;
		}
	}

	private static IEnumerable<string> GetMiniGameDataCandidatePaths()
	{
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", "MiniGameData.txt");
		DirectoryInfo directoryInfo = new DirectoryInfo(AppContext.BaseDirectory);
		DirectoryInfo root = directoryInfo.Parent;
		if (root != null)
		{
			yield return Path.Combine(root.FullName, "Servers", "OS Free Realms", "Client", "Resources", "MiniGameData.txt");
			yield return Path.Combine(root.FullName, "Servers", "Kaine's Server", "Client", "Resources", "MiniGameData.txt");
		}
	}

	private static bool TryParseMiniGameDataRow(string line, out MiniGameDataRow row)
	{
		row = null;
		if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
		{
			return false;
		}
		string[] array = line.Split('^');
		if (array.Length < 6 || !int.TryParse(array[0], out var result) || !int.TryParse(array[1], out var result2))
		{
			return false;
		}
		int.TryParse(array[2], out var result3);
		int.TryParse(array[3], out var result4);
		int.TryParse(array[4], out var result5);
		int.TryParse(array[5], out var result6);
		row = new MiniGameDataRow(result, result2, result3, result4, result5, result6);
		return true;
	}
}
