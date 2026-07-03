using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketTunneledClientWorldPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PacketTunneledClientWorldPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
	{
		if (!PacketTunneledClientWorldPacket.TryDeserialize(data, out PacketTunneledClientWorldPacket value))
		{
			_logger.LogError("Failed to deserialize {packet}.", "PacketTunneledClientWorldPacket");
			return false;
		}
		PacketReader reader = new PacketReader(value.Payload);
		if (!reader.TryRead(out short result))
		{
			_logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(data));
			return false;
		}
		MinigameTileEventReader.Incoming(connection, worldTunnel: true, result, value.Payload, "world-tunnel-dispatch");
		return result switch
		{
			26 => BaseCommandPacketHandler.HandlePacket(connection, reader), 
			39 => MiniGamePacketHandler.HandlePacket(connection, reader, value.Payload, worldTunnel: true), 
			58 => PacketWorldTeleportRequestHandler.HandlePacket(connection, value.Payload), 
			66 => PacketBaseInGamePurchaseHandler.HandlePacket(connection, reader), 
			88 => PacketSetLocaleHandler.HandlePacket(connection, value.Payload), 
			102 => BaseLobbyGameDefinitionPacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			117 => OneTimeSessionPacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			127 => BaseHousingPacketHandler.HandlePacket(connection, reader), 
			141 => MatchmakingPacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			156 => BaseFotomatPacketHandler.HandlePacket(connection, reader), 
			167 => BaseActivityServicePacketHandler.HandlePacket(connection, reader, value.Payload, worldTunnel: true), 
			175 => ClientActivityLaunchPacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			180 => InviteAndStartMiniGamePacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			194 => WallOfDataBasePacketHandler.HandlePacket(connection, reader, worldTunnel: true), 
			_ => RawTcgPacketHandler.HandlePacket(connection, result, value.Payload, "world-unhandled"), 
		};
	}
}
