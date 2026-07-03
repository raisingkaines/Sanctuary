using System;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class WallOfDataBasePacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("WallOfDataBasePacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		if (!reader.TryRead(out byte result))
		{
			_logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
			return false;
		}
		MinigameDiagnosticsLog.Info("packet-received", "WallOfDataBasePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("family", "WallOfData"), MinigameDiagnosticsLog.Field("type", result), MinigameDiagnosticsLog.HexField("remaining", reader.RemainingSpan));
		MinigameTileEventReader.Decoded(connection, worldTunnel, 194, "WallOfDataBasePacketHandler", "WallOfData", result, reader.RemainingSpan);
		switch (result)
		{
		case 4:
			return WallOfDataUIEventPacketHandler.HandlePacket(connection, reader.RemainingSpan, worldTunnel);
		case 2:
			LogInputState(connection, reader.RemainingSpan, worldTunnel);
			return false;
		default:
			return false;
		}
	}

	private static void LogInputState(GatewayConnection connection, ReadOnlySpan<byte> data, bool worldTunnel)
	{
		if (data.Length < 4)
		{
			return;
		}
		int num = BitConverter.ToInt32(data.Slice(0, 4));
		if (num < 0 || num > 32 || data.Length != 4 + num * 8)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			int num2 = 4 + i * 8;
			int num3 = BitConverter.ToInt32(data.Slice(num2, 4));
			int value = BitConverter.ToInt32(data.Slice(num2 + 4, 4));
			if (i > 0)
			{
				stringBuilder.Append(',');
				stringBuilder2.Append(',');
			}
			stringBuilder.Append(num3).Append(':').Append(value);
			stringBuilder2.Append((num3 >= 32 && num3 <= 126) ? ((char)num3) : '.');
		}
		MinigameDiagnosticsLog.Info("WallOfDataInputStateObserved", "WallOfDataBasePacketHandler", connection.Player?.Guid ?? 0, worldTunnel, MinigameDiagnosticsLog.Field("keyCount", num), MinigameDiagnosticsLog.Field("keyCodes", stringBuilder.ToString()), MinigameDiagnosticsLog.Field("keyChars", stringBuilder2.ToString()), MinigameDiagnosticsLog.Field("sourceEvent", "WallOfDataType2"), MinigameDiagnosticsLog.Field("note", "Decoded as keyboard/input state; useful to rule out this packet as PlayButtonClicked."), MinigameDiagnosticsLog.HexField("payload", data));
	}
}
