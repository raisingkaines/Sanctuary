using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class AnnouncementPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AnnouncementPacketHandler");
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
	{
		if (!reader.TryRead(out byte result))
		{
			_logger.LogError("Failed to read announcement sub-opcode.");
			return false;
		}
		if (result != 1)
		{
			_logger.LogWarning("Unhandled announcement packet. Type={type}", result);
			return false;
		}
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)193);
		packetWriter.Write((byte)2);
		packetWriter.Write(0);
		connection.Send(new PacketTunneledClientPacket
		{
			Reliable = true,
			Payload = packetWriter.Buffer
		});
		_logger.LogDebug("Answered AnnouncementDataRequestPacket. Player={player}", connection.Player?.Guid);
		return true;
	}
}
