using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class LobbyGameDefinitionPacketDefinitionsRequestHandler
{
	private const int TradingCardGameNameId = 3388;

	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("LobbyGameDefinitionPacketDefinitionsRequestHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, bool worldTunnel)
	{
		ulong num = connection.Player?.Guid ?? 0;
		_logger.LogInformation("LobbyGameDefinitionRequest. Player={player}, WorldTunnel={worldTunnel}", num, worldTunnel);
		MinigameDiagnosticsLog.Info("lobby-game-definition-request", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel);
		TcgDetailUiPatch.LogOuterGamesGridDiagnostics(num, worldTunnel, "LobbyGameDefinitionRequest");
		LobbyGameDefinition lobbyGameDefinition = new LobbyGameDefinition();
		lobbyGameDefinition.GameEntries.Add(1, new LobbyGameDefinition.LobbyGameEntry
		{
			Id = 1,
			Type = 1,
			Unknown = 37,
			NameId = 3030
		});
		AddTcgEntry(lobbyGameDefinition, 727, 23, 7589, 3388, num, worldTunnel, "Native TCG MiniGameData Row");
		using PacketWriter packetWriter = new PacketWriter();
		lobbyGameDefinition.Serialize(packetWriter);
		using PacketWriter packetWriter2 = new PacketWriter();
		packetWriter2.Write((short)102);
		packetWriter2.Write((short)2);
		packetWriter2.WritePayload(packetWriter.Buffer);
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter2.Buffer);
		if (worldTunnel)
		{
			TcgTunnelSend.Send(connection, worldTunnel: false, packetWriter2.Buffer);
			MinigameDiagnosticsLog.Info("lobby-game-definition-client-tunnel-mirror", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("reason", "activity-portal-tcg-selection"), MinigameDiagnosticsLog.Field("bytes", packetWriter2.Buffer.Length));
		}
		_logger.LogInformation("LobbyGameDefinitionResponse. Player={player}, WorldTunnel={worldTunnel}, Payload={payload}", num, worldTunnel, Convert.ToHexString(packetWriter2.Buffer));
		MinigameDiagnosticsLog.Info("lobby-game-definition-response", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("activity", 24), MinigameDiagnosticsLog.Field("activityType", 23), MinigameDiagnosticsLog.Field("category", 11), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("nativeMiniGameId", 727), MinigameDiagnosticsLog.Field("portalType", 11), MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("visibleNameLocaleId", 3388), MinigameDiagnosticsLog.Field("nativeCodeStringNameId", 3388), MinigameDiagnosticsLog.Field("nativeCodeStringDescriptionId", 401571), MinigameDiagnosticsLog.Field("imageSetId", 7589), MinigameDiagnosticsLog.Field("acceptedTreasureWarAliasCategoryId", 26), MinigameDiagnosticsLog.Field("normalizedDetailCategoryId", 11), MinigameDiagnosticsLog.Field("detailCarouselRowsAdvertisedAsBrowserGames", false), MinigameDiagnosticsLog.Field("tcgDefinitionNameIds", BuildTcgDefinitionNameIdList()), MinigameDiagnosticsLog.Field("rootCause", "Browser_V2 AddGame definitions are the outer Games menu tile feed. Advertising competing TCG identities here made the outer TCG tile resolve to Treasure War."), MinigameDiagnosticsLog.Field("fix", "Keep the browser definition map limited to the canonical native TCG MiniGameData row 727. The MinigameDetail carousel rows are supplied only by opcode 167 and Populate(11); category 26 is accepted only as a legacy click alias and normalized back to category 11."), MinigameDiagnosticsLog.HexField("payload", packetWriter2.Buffer));
		MinigameDiagnosticsLog.Info("lobby-definition-menu-only", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("activity", 7));
		MiniGamePacketHandler.SendTradingCardActivityUnlocks(connection, worldTunnel, num);
		MinigameDiagnosticsLog.Info("lobby-definition-tcg-unlocks-sent", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("reason", "populate-activities-category-11"));
		MinigameDiagnosticsLog.Info("lobby-definition-detail-patch-deferred", "LobbyGameDefinitionPacketDefinitionsRequestHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("reason", "wait-for-list-queues"));
		return true;
	}

	private static void AddTcgEntry(LobbyGameDefinition lobbyGameDefinition, int id, int type, int unknown, int nameId, ulong playerGuid, bool worldTunnel, string debugName)
	{
		lobbyGameDefinition.GameEntries.Add(id, new LobbyGameDefinition.LobbyGameEntry
		{
			Id = id,
			Type = type,
			Unknown = unknown,
			NameId = nameId
		});
		LogLobbyDefinitionRow(playerGuid, worldTunnel, id, type, unknown, nameId, debugName);
	}

	private static void LogLobbyDefinitionRow(ulong playerGuid, bool worldTunnel, int id, int type, int unknown, int nameId, string debugName)
	{
		TcgDetailUiPatch.LocaleLookupResult localeLookupResult = TcgDetailUiPatch.ResolveUiGetStringByIdForDiagnostics(nameId);
		MinigameDiagnosticsLog.Info("TcgLobbyDefinitionWireRow", "LobbyGameDefinitionPacketDefinitionsRequestHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("context", "outer Games grid lobby definition"), MinigameDiagnosticsLog.Field("wireOrder", "dictionaryKey,id,type,iconOrGroup,nameToken"), MinigameDiagnosticsLog.Field("dictionaryKeySigned", id), MinigameDiagnosticsLog.Field("dictionaryKeyUnsigned", Unsigned(id)), MinigameDiagnosticsLog.Field("dictionaryKeyHex", Hex(id)), MinigameDiagnosticsLog.Field("idSigned", id), MinigameDiagnosticsLog.Field("idUnsigned", Unsigned(id)), MinigameDiagnosticsLog.Field("idHex", Hex(id)), MinigameDiagnosticsLog.Field("typeSigned", type), MinigameDiagnosticsLog.Field("typeUnsigned", Unsigned(type)), MinigameDiagnosticsLog.Field("typeHex", Hex(type)), MinigameDiagnosticsLog.Field("imageSetId", unknown), MinigameDiagnosticsLog.Field("iconOrGroupSigned", unknown), MinigameDiagnosticsLog.Field("iconOrGroupUnsigned", Unsigned(unknown)), MinigameDiagnosticsLog.Field("iconOrGroupHex", Hex(unknown)), MinigameDiagnosticsLog.Field("nameSigned", nameId), MinigameDiagnosticsLog.Field("nameUnsigned", Unsigned(nameId)), MinigameDiagnosticsLog.Field("nameHex", Hex(nameId)), MinigameDiagnosticsLog.Field("nameResolved", localeLookupResult.Resolved), MinigameDiagnosticsLog.Field("nameResolvedString", localeLookupResult.Text), MinigameDiagnosticsLog.Field("nameResolvedSource", localeLookupResult.Source), MinigameDiagnosticsLog.Field("nameCodeStringMappingExists", localeLookupResult.CodeStringMappingExists), MinigameDiagnosticsLog.Field("nameCodeStringMappingKey", localeLookupResult.CodeStringMappingKey), MinigameDiagnosticsLog.Field("detailImage", "tcg_lobby_detail.dds"), MinigameDiagnosticsLog.Field("detailImagePublicAssetUrl", TcgDetailUiPatch.BuildPublicAssetUrlForDiagnostics("tcg_lobby_detail.dds")), MinigameDiagnosticsLog.Field("detailImageExists", TcgDetailUiPatch.ClientResourceAssetExistsForDiagnostics("tcg_lobby_detail.dds")), MinigameDiagnosticsLog.Field("thumbnailImage", "icon_category_minigames_tradingcardgame_128.dds"), MinigameDiagnosticsLog.Field("thumbnailImagePublicAssetUrl", TcgDetailUiPatch.BuildPublicAssetUrlForDiagnostics("icon_category_minigames_tradingcardgame_128.dds")), MinigameDiagnosticsLog.Field("thumbnailImageExists", TcgDetailUiPatch.ClientResourceAssetExistsForDiagnostics("icon_category_minigames_tradingcardgame_128.dds")), MinigameDiagnosticsLog.Field("debugName", debugName), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("activityId", 24), MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23), MinigameDiagnosticsLog.Field("miniGameId", 727), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false));
	}

	private static string BuildTcgDefinitionNameIdList()
	{
		return $"{727}={3388}";
	}

	private static uint Unsigned(int value)
	{
		return (uint)value;
	}

	private static string Hex(int value)
	{
		return "0x" + Unsigned(value).ToString("X8");
	}
}
