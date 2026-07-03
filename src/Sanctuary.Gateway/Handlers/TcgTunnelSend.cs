using System;
using Sanctuary.Packet;

namespace Sanctuary.Gateway.Handlers;

internal static class TcgTunnelSend
{
	internal static void Send(GatewayConnection connection, bool worldTunnel, byte[] buffer)
	{
		short num = (short)((buffer.Length >= 2) ? BitConverter.ToInt16(buffer, 0) : (-1));
		MinigameDiagnosticsLog.Info("tunneled-packet-sent", "TcgTunnelSend", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("opcode", num), MinigameDiagnosticsLog.Field("bytes", buffer.Length), MinigameDiagnosticsLog.HexField("payload", buffer));
		MinigameTileEventReader.Outgoing(connection, worldTunnel, num, buffer, "server-tunnel-send");
		if (worldTunnel)
		{
			connection.Send(new PacketTunneledClientWorldPacket
			{
				Reliable = true,
				Payload = buffer
			});
		}
		else
		{
			connection.Send(new PacketTunneledClientPacket
			{
				Reliable = true,
				Payload = buffer
			});
		}
	}
}
