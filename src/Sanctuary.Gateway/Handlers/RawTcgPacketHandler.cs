using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class RawTcgPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RawTcgPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, short opcode, ReadOnlySpan<byte> payload, string tunnel)
	{
		string text;
		try
		{
			text = new PacketReader(payload.ToArray()).ReadTunneledPacketName();
		}
		catch (Exception ex)
		{
			text = $"unknown ({ex.GetType().Name}: {ex.Message})";
		}
		_logger.LogInformation("TCG/minigame packet observed. Tunnel={tunnel}, Player={player}, Opcode={opcode}, Name={name}, Bytes={bytes}", tunnel, connection.Player?.Guid, opcode, text, Convert.ToHexString(payload));
		MinigameDiagnosticsLog.Warn("raw-unhandled-packet", "RawTcgPacketHandler", connection.Player?.Guid ?? 0, MinigameDiagnosticsLog.Field("tunnel", tunnel), MinigameDiagnosticsLog.Field("opcode", opcode), MinigameDiagnosticsLog.Field("packetName", text), MinigameDiagnosticsLog.HexField("payload", payload));
		return true;
	}
}
