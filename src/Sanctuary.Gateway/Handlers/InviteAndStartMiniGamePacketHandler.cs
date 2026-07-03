using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class InviteAndStartMiniGamePacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InviteAndStartMiniGamePacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		if (!reader.TryRead(out int result))
		{
			_logger.LogError("Failed to read invite/start minigame packet type.");
			MinigameDiagnosticsLog.Warn("packet-read-failed", "InviteAndStartMiniGamePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "InviteAndStartMiniGame"), MinigameDiagnosticsLog.Field("reason", "missing packet type"));
			return false;
		}
		ulong num = connection.Player?.Guid ?? 0;
		_logger.LogInformation("InviteAndStartMiniGameRequest. Type={type}, Player={player}, WorldTunnel={worldTunnel}, Remaining={remaining}", result, num, worldTunnel, Convert.ToHexString(reader.RemainingSpan));
		MinigameDiagnosticsLog.Info("packet-received", "InviteAndStartMiniGamePacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("family", "InviteAndStartMiniGame"), MinigameDiagnosticsLog.Field("type", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 180, "InviteAndStartMiniGamePacketHandler", "InviteAndStartMiniGame", result, reader.RemainingSpan);
		return result switch
		{
			1 => HandleInvite(connection, reader, worldTunnel, num), 
			3 => HandleStartGame(connection, reader, worldTunnel, num), 
			_ => LogUnhandled(result, num, worldTunnel), 
		};
	}

	private static bool HandleInvite(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)180);
		packetWriter.Write(1);
		packetWriter.Write(playerGuid);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		_logger.LogInformation("InviteAndStartMiniGame invite ack. Player={player}", playerGuid);
		MinigameDiagnosticsLog.Info("invite-ack-sent", "InviteAndStartMiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool HandleStartGame(GatewayConnection connection, PacketReader reader, bool worldTunnel, ulong playerGuid)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)180);
		packetWriter.Write(3);
		packetWriter.Write(playerGuid);
		packetWriter.Write(7);
		packetWriter.Write(0);
		MatchmakingPacketHandler.Send(connection, worldTunnel, packetWriter.Buffer);
		ClientActivityLaunchPacketHandler.CompleteTcgLaunch(connection, worldTunnel, playerGuid);
		_logger.LogInformation("InviteAndStartMiniGame start ack. Player={player}", playerGuid);
		MinigameDiagnosticsLog.Info("start-game-ack-sent", "InviteAndStartMiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("activity", 7), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		return true;
	}

	private static bool LogUnhandled(int packetType, ulong playerGuid, bool worldTunnel)
	{
		MinigameDiagnosticsLog.Warn("unhandled-packet", "InviteAndStartMiniGamePacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("family", "InviteAndStartMiniGame"), MinigameDiagnosticsLog.Field("type", packetType));
		return true;
	}
}
