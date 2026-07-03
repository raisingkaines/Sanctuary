using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseLobbyGameDefinitionPacketHandler
{
	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BaseLobbyGameDefinitionPacketHandler");
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		if (!reader.TryRead(out short result))
		{
			_logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
			return false;
		}
		if (result == 1)
		{
			return LobbyGameDefinitionPacketDefinitionsRequestHandler.HandlePacket(connection, worldTunnel);
		}
		_logger.LogInformation("Unhandled lobby game definition packet. Type={type}, Player={player}, WorldTunnel={worldTunnel}", result, connection.Player?.Guid ?? 0, worldTunnel);
		return false;
	}
}
