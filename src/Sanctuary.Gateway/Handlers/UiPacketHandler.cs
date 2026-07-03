using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class UiPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("UiPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, ReadOnlySpan<byte> payload)
	{
		if (!reader.TryRead(out byte result))
		{
			_logger.LogError("Failed to read UI sub-opcode. Bytes={bytes}", Convert.ToHexString(payload));
			return false;
		}
		MinigameTileEventReader.Decoded(connection, false, 47, "UiPacketHandler", "Ui", result, reader.RemainingSpan);
		if (result == 13)
		{
			return HandleSelectedQuestLocked(connection, reader);
		}
		return RawTcgPacketHandler.HandlePacket(connection, 47, payload, "client-ui-unhandled");
	}

	private static bool HandleSelectedQuestLocked(GatewayConnection connection, PacketReader reader)
	{
		byte result = 0;
		reader.TryRead(out result);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)13);
		packetWriter.Write(result);
		connection.Send(new PacketTunneledClientPacket
		{
			Reliable = true,
			Payload = packetWriter.Buffer
		});
		_logger.LogDebug("Answered SelectedQuestLockedPacket. Player={player}, Locked={locked}", connection.Player?.Guid, result);
		return true;
	}
}
