using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MatchmakingPacketHandler
{
	private sealed class MatchmakingQueue : ISerializableType
	{
		public static readonly MatchmakingQueue Tcg = new MatchmakingQueue();

		public void Serialize(PacketWriter writer)
		{
			writer.Write("Trading Card Game");
			writer.Write(0);
			writer.Write(1);
			writer.Write(2);
			writer.Write(0);
			writer.Write(0);
			writer.Write(0);
			writer.Write(1);
			writer.Write(1);
			writer.Write(1);
			writer.Write(1);
			writer.Write(7);
			writer.Write(41);
			writer.Write(0);
			writer.Write(0);
			writer.Write(0);
			writer.Write("Play the Free Realms Trading Card Game.");
			writer.Write(9867);
			writer.Write(0);
		}
	}

	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MatchmakingPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		if (!reader.TryRead(out short result))
		{
			_logger.LogError("Failed to read matchmaking packet type.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "MatchmakingPacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "Matchmaking"), MinigameDiagnosticsLog.Field("reason", "missing packet type"));
			return false;
		}
		_logger.LogInformation("MatchmakingRequest. Type={type}, Player={player}, WorldTunnel={worldTunnel}, Remaining={remaining}", result, connection.Player?.Guid ?? 0, worldTunnel, Convert.ToHexString(reader.RemainingSpan));
		MinigameDiagnosticsLog.Info("packet-received", "MatchmakingPacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "Matchmaking"), MinigameDiagnosticsLog.Field("type", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 141, "MatchmakingPacketHandler", "Matchmaking", result, reader.RemainingSpan);
		return result switch
		{
			1 => HandleListQueuesRequest(connection, reader, worldTunnel), 
			3 => HandleAddMatchRequest(connection, reader, worldTunnel), 
			5 => HandleClearMatchRequest(connection, worldTunnel), 
			6 => HandleCancelMatchRequest(connection, worldTunnel), 
			9 => HandleMatchInvitationRequest(connection, reader, worldTunnel), 
			10 => HandleMatchInvitationResponse(connection, reader, worldTunnel), 
			12 => HandleSelectQueueForUser(connection, reader, worldTunnel), 
			13 => HandleQueueStatsRequest(connection, reader, worldTunnel), 
			_ => HandleUnknown(connection, result, worldTunnel), 
		};
	}

	private static bool HandleListQueuesRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		TcgMatchmakingState.ListQueueObservation listQueueObservation = TcgMatchmakingState.RecordListQueuesRequest(playerGuid);
		LogUiEvent("GamesMenuOpened", playerGuid, worldTunnel, "ListQueuesRequest", 0, 0, string.Empty, string.Empty, string.Empty, didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
		LogUiEvent("ListQueuesRequest", playerGuid, worldTunnel, "GamesMenu", 0, 0, string.Empty, string.Empty, string.Empty, didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
		MinigameDiagnosticsLog.Info("list-queues-request-observed", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("count", listQueueObservation.Count), MinigameDiagnosticsLog.Field("threshold", listQueueObservation.Threshold), MinigameDiagnosticsLog.Field("windowSeconds", listQueueObservation.WindowSeconds), MinigameDiagnosticsLog.Field("cooldownRemainingSeconds", listQueueObservation.CooldownRemainingSeconds), MinigameDiagnosticsLog.Field("repeatedWithoutClientLaunch", listQueueObservation.ShouldAutoLaunch));
		TcgDetailUiPatch.LogOuterGamesGridDiagnostics(playerGuid, worldTunnel, "ListQueuesRequest");
		SendListQueuesResponse(connection, worldTunnel, playerGuid);
		SendMatchmakingServerStatus(connection, worldTunnel, playerGuid, 41, ready: true);
		if (worldTunnel)
		{
			MinigameDiagnosticsLog.Info("list-queues-client-tunnel-mirror", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
			SendListQueuesResponse(connection, worldTunnel: false, playerGuid);
			SendMatchmakingServerStatus(connection, worldTunnel: false, playerGuid, 41, ready: true);
		}
		MiniGamePacketHandler.SendTradingCardActivityUnlocks(connection, worldTunnel, playerGuid);
		LogUiEvent("GameGridLoaded", playerGuid, worldTunnel, "MiniGameUnlocks", 0, 0, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
		bool flag = listQueueObservation.Count >= listQueueObservation.Threshold;
		double selectionAgeMs;
		int activityCategoryId;
		bool flag2 = TcgMatchmakingState.TryGetRecentTcgTileSelection(playerGuid, out selectionAgeMs, out activityCategoryId);
		bool flag3 = flag2;
		string text = (flag3 ? "list-queues-after-tcg-selection" : (flag ? "list-queues-repeated-menu-datasource-preload" : "list-queues-menu-datasource-preload"));
		MinigameDiagnosticsLog.Info("tcg-detail-populate-decision", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("count", listQueueObservation.Count), MinigameDiagnosticsLog.Field("threshold", listQueueObservation.Threshold), MinigameDiagnosticsLog.Field("repeatedDetailRequest", flag), MinigameDiagnosticsLog.Field("recentTcgTileSelection", flag2), MinigameDiagnosticsLog.Field("selectionAgeMs", selectionAgeMs), MinigameDiagnosticsLog.Field("selectedTcgCategoryId", activityCategoryId), MinigameDiagnosticsLog.Field("populateDetail", flag3), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("rootCauseCandidate", "The post-click ListQueuesRequest arrives after BrowserV2 SelectGame; treating that request as menu-only sends selectedId=0/category preload and can visibly reset the detail panel back to No Game Selected."), MinigameDiagnosticsLog.Field("fixCandidate", "If ListQueuesRequest is inside the short real tile-selection window, preserve the selected TCG category and resend the datasource plus MinigameDetail:Show/Populate(category), without launching TCG or sending an invite."));
		if (flag3)
		{
			if (PacketReadyActivityDefinitions.TrySuppressOldTcgOverride("TcgDetailUiPatch", playerGuid, worldTunnel, "MenuDatasourcePreload", text + "-" + activityCategoryId, 11, "41,603,604,602"))
			{
				PacketReadyActivityDefinitions.TryShowCategoryDetailFromStartupList(connection, worldTunnel, playerGuid, 11, "list-queues-after-tcg-selection-packet-ready-" + activityCategoryId);
				LogUiEvent("TcgCategoryDataSent", playerGuid, worldTunnel, "ListQueuesResponseAfterTcgSelectionPacketReady", 727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
				LogUiEvent("GameGridLoaded", playerGuid, worldTunnel, "ListQueuesResponse", 0, 0, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
				MinigameDiagnosticsLog.Info("list-queues-menu-only", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
				return true;
			}
			TcgDetailUiPatch.SendDatasourceAndPopulateAfterDatasourceDelay(connection, worldTunnel, playerGuid, text + "-" + activityCategoryId, activityCategoryId);
			LogUiEvent("TcgCategoryDataSent", playerGuid, worldTunnel, "ListQueuesResponseAfterTcgSelection", 727, 24, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
		}
		else
		{
			if (PacketReadyActivityDefinitions.TrySuppressOldTcgOverride("TcgDetailUiPatch", playerGuid, worldTunnel, "MenuDatasourcePreload", text, 11, "41,603,604,602"))
			{
				LogUiEvent("GameGridLoaded", playerGuid, worldTunnel, "ListQueuesResponse", 0, 0, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
				MinigameDiagnosticsLog.Info("list-queues-menu-only", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
				return true;
			}
			TcgDetailUiPatch.SendWarmDatasourcePreload(connection, worldTunnel, playerGuid, text, "ListQueuesRequest");
		}
		LogUiEvent("GameGridLoaded", playerGuid, worldTunnel, "ListQueuesResponse", 0, 0, "tcg", "Trading Card Game", "TradingCardGameHandler", didLaunch: false, didSendInvite: false, didShowNoGameSelected: false);
		MinigameDiagnosticsLog.Info("list-queues-menu-only", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
		return true;
	}

	private static void LogUiEvent(string eventName, ulong playerGuid, bool worldTunnel, string sourceEvent, int selectedId, int activityId, string gameKey, string gameName, string handler, bool didLaunch, bool didSendInvite, bool didShowNoGameSelected)
	{
		string value = ((selectedId > 0) ? gameName : string.Empty);
		MinigameDiagnosticsLog.Info(eventName, "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("selectedGame", value), MinigameDiagnosticsLog.Field("selectedId", selectedId), MinigameDiagnosticsLog.Field("activityId", activityId), MinigameDiagnosticsLog.Field("gameKey", gameKey), MinigameDiagnosticsLog.Field("gameName", gameName), MinigameDiagnosticsLog.Field("categoryId", 11), MinigameDiagnosticsLog.Field("miniGameId", 387), MinigameDiagnosticsLog.Field("handler", handler), MinigameDiagnosticsLog.Field("sourceEvent", sourceEvent), MinigameDiagnosticsLog.Field("didLaunch", didLaunch), MinigameDiagnosticsLog.Field("didSendInvite", didSendInvite), MinigameDiagnosticsLog.Field("didShowNoGameSelected", didShowNoGameSelected));
	}

	private static void SendListQueuesResponse(GatewayConnection connection, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)141);
		packetWriter.Write((short)2);
		packetWriter.Write(new Dictionary<int, MatchmakingQueue> { [41] = MatchmakingQueue.Tcg });
		Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("ListQueuesResponse. Player={player}, WorldTunnel={worldTunnel}, Payload={payload}", playerGuid, worldTunnel, Convert.ToHexString(packetWriter.Buffer));
		MinigameDiagnosticsLog.Info("list-queues-response", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("framing", "dictionary-without-player-guid"), MinigameDiagnosticsLog.Field("schema", "lobby-fixed-columns-0-through-19"), MinigameDiagnosticsLog.Field("name", "Trading Card Game"), MinigameDiagnosticsLog.Field("minPlayers", 1), MinigameDiagnosticsLog.Field("maxPlayers", 2), MinigameDiagnosticsLog.Field("currentPlayers", 0), MinigameDiagnosticsLog.Field("averageWaitSeconds", 0), MinigameDiagnosticsLog.Field("description", "Play the Free Realms Trading Card Game."), MinigameDiagnosticsLog.Field("icon", 9867), MinigameDiagnosticsLog.Field("memberOnly", 0), MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static bool HandleAddMatchRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		int result = 41;
		reader.TryRead(out result);
		TcgMatchmakingState.JoinQueue(playerGuid, result);
		MinigameDiagnosticsLog.Info("matchmaking-join-queue", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result));
		using (PacketWriter packetWriter = new PacketWriter())
		{
			packetWriter.Write((short)141);
			packetWriter.Write((short)4);
			packetWriter.Write(playerGuid);
			packetWriter.Write(result);
			packetWriter.Write(0);
			packetWriter.Write(1);
			Send(connection, worldTunnel, packetWriter.Buffer);
		}
		SendMatchmakingServerStatus(connection, worldTunnel, playerGuid, result, ready: true);
		if (result == 41)
		{
			if (TcgMatchmakingState.TryBeginLaunch(playerGuid, result, out var cooldownRemainingSeconds))
			{
				MinigameDiagnosticsLog.Info("matchmaking-join-queue-launch", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result), MinigameDiagnosticsLog.Field("reason", "Lobby:EnterQueue called Matchmaking.JoinQueue"));
				ClientActivityLaunchPacketHandler.CompleteTcgLaunch(connection, worldTunnel, playerGuid);
			}
			else
			{
				MinigameDiagnosticsLog.Info("matchmaking-join-queue-launch-cooldown", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result), MinigameDiagnosticsLog.Field("cooldownRemainingSeconds", cooldownRemainingSeconds));
			}
		}
		_logger.LogDebug("AddMatchRequestResponse. Player={player}, Queue={queue}", playerGuid, result);
		return true;
	}

	private static bool HandleClearMatchRequest(GatewayConnection connection, bool worldTunnel)
	{
		ulong playerGuid = connection.Player?.Guid ?? 0;
		TcgMatchmakingState.LeaveQueue(playerGuid);
		MinigameDiagnosticsLog.Info("matchmaking-leave-queue", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
		SendMatchmakingServerStatus(connection, worldTunnel, playerGuid, 41, ready: false);
		return true;
	}

	private static bool HandleCancelMatchRequest(GatewayConnection connection, bool worldTunnel)
	{
		return HandleClearMatchRequest(connection, worldTunnel);
	}

	private static bool HandleMatchInvitationRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		using PacketWriter packetWriter = new PacketWriter();
		MinigameDiagnosticsLog.Info("match-invitation-request", "MatchmakingPacketHandler", playerGuid, worldTunnel);
		packetWriter.Write((short)141);
		packetWriter.Write((short)10);
		packetWriter.Write(playerGuid);
		packetWriter.Write(0);
		Send(connection, worldTunnel, packetWriter.Buffer);
		return true;
	}

	private static bool HandleMatchInvitationResponse(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		TcgMatchmakingState.JoinQueue(playerGuid, 41);
		MinigameDiagnosticsLog.Info("match-invitation-response", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41));
		SendMatchmakingServerStatus(connection, worldTunnel, playerGuid, 41, ready: true);
		return true;
	}

	private static bool HandleSelectQueueForUser(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		int result = 41;
		reader.TryRead(out result);
		TcgMatchmakingState.JoinQueue(playerGuid, result);
		MinigameDiagnosticsLog.Info("select-queue-for-user", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result));
		SendListQueuesResponse(connection, worldTunnel, playerGuid);
		SendMatchmakingServerStatus(connection, worldTunnel, playerGuid, result, ready: true);
		return true;
	}

	private static bool HandleQueueStatsRequest(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		ulong playerGuid = GetPlayerGuid(connection, reader);
		int result = 41;
		reader.TryRead(out result);
		MinigameDiagnosticsLog.Info("queue-stats-request", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", result));
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)141);
		packetWriter.Write((short)14);
		packetWriter.Write(playerGuid);
		packetWriter.Write(result);
		packetWriter.Write(0);
		packetWriter.Write(0);
		Send(connection, worldTunnel, packetWriter.Buffer);
		return true;
	}

	private static void SendMatchmakingServerStatus(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int queueId, bool ready)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)141);
		packetWriter.Write((short)15);
		packetWriter.Write(playerGuid);
		packetWriter.Write(queueId);
		packetWriter.Write(ready ? 1 : 0);
		Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("matchmaking-server-status", "MatchmakingPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("queue", queueId), MinigameDiagnosticsLog.Field("ready", ready), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static bool HandleUnknown(GatewayConnection connection, short packetType, bool worldTunnel)
	{
		_logger.LogDebug("Unhandled matchmaking packet type={type}", packetType);
		MinigameDiagnosticsLog.Warn("unhandled-packet", "MatchmakingPacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "Matchmaking"), MinigameDiagnosticsLog.Field("type", packetType));
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)141);
		packetWriter.Write(packetType);
		Send(connection, worldTunnel, packetWriter.Buffer);
		return true;
	}

	private static ulong GetPlayerGuid(GatewayConnection connection, PacketReader reader)
	{
		ulong result = connection.Player?.Guid ?? 0;
		if (reader.TryRead(out ulong result2))
		{
			result = result2;
		}
		return result;
	}

	internal static void Send(GatewayConnection connection, bool worldTunnel, byte[] buffer)
	{
		TcgTunnelSend.Send(connection, worldTunnel, buffer);
	}
}
