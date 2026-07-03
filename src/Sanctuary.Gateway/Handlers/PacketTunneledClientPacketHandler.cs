using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketTunneledClientPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PacketTunneledClientPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
	{
		if (!PacketTunneledClientPacket.TryDeserialize(data, out PacketTunneledClientPacket value))
		{
			_logger.LogError("Failed to deserialize {packet}.", "PacketTunneledClientPacket");
			return false;
		}
		PacketReader reader = new PacketReader(value.Payload);
		if (!reader.TryRead(out short result))
		{
			_logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(data));
			return false;
		}
		MinigameTileEventReader.Incoming(connection, worldTunnel: false, result, value.Payload, "client-tunnel-dispatch");
		return result switch
		{
			10 => PacketClientFinishedLoadingHandler.HandlePacket(connection), 
			13 => PacketClientIsReadyHandler.HandlePacket(connection), 
			15 => BaseChatPacketHandler.HandlePacket(connection, reader), 
			26 => BaseCommandPacketHandler.HandlePacket(connection, reader), 
			35 => BasePlayerUpdatePacketHandler.HandlePacket(connection, reader), 
			36 => BaseAbilityPacketHandler.HandlePacket(connection, reader), 
			47 => UiPacketHandler.HandlePacket(connection, reader, value.Payload), 
			39 => MiniGamePacketHandler.HandlePacket(connection, reader, value.Payload, worldTunnel: false), 
			42 => BaseInventoryPacketHandler.HandlePacket(connection, reader), 
			52 => PacketGameTimeSyncHandler.HandlePacket(connection, value.Payload), 
			66 => PacketBaseInGamePurchaseHandler.HandlePacket(connection, reader), 
			67 => BaseQuickChatPacketHandler.HandlePacket(connection, reader), 
			90 => PacketZoneTeleportRequestHandler.HandlePacket(connection, value.Payload), 
			105 => PacketClientMetricsHandler.HandlePacket(connection, value.Payload), 
			109 => PacketClientLogHandler.HandlePacket(connection, value.Payload), 
			117 => OneTimeSessionPacketHandler.HandlePacket(connection, reader, worldTunnel: false), 
			122 => PacketZoneSafeTeleportRequestHandler.HandlePacket(connection, value.Payload), 
			125 => PlayerUpdatePacketUpdatePositionHandler.HandlePacket(connection, value.Payload), 
			126 => PlayerUpdatePacketCameraUpdateHandler.HandlePacket(connection, value.Payload), 
			127 => BaseHousingPacketHandler.HandlePacket(connection, reader), 
			141 => MatchmakingPacketHandler.HandlePacket(connection, reader, worldTunnel: false), 
			152 => BasePlayerTitlePacketHandler.HandlePacket(connection, reader), 
			156 => BaseFotomatPacketHandler.HandlePacket(connection, reader), 
			164 => PlayerUpdatePacketJumpHandler.HandlePacket(connection, value.Payload), 
			165 => BaseCoinStorePacketHandler.HandlePacket(connection, reader), 
			167 => BaseActivityServicePacketHandler.HandlePacket(connection, reader, value.Payload, worldTunnel: false), 
			168 => MountBasePacketHandler.HandlePacket(connection, reader), 
			169 => PacketClientInitializationDetailsHandler.HandlePacket(connection, value.Payload), 
			175 => ClientActivityLaunchPacketHandler.HandlePacket(connection, reader, worldTunnel: false), 
			180 => InviteAndStartMiniGamePacketHandler.HandlePacket(connection, reader, worldTunnel: false), 
			192 => BaseNameChangePacketHandler.HandlePacket(connection, reader), 
			193 => AnnouncementPacketHandler.HandlePacket(connection, reader), 
			194 => WallOfDataBasePacketHandler.HandlePacket(connection, reader, worldTunnel: false), 
			_ => RawTcgPacketHandler.HandlePacket(connection, result, value.Payload, "client-unhandled"), 
		};
	}
}
