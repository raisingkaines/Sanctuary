using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Game;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientIsReadyHandler
{
	private static ILogger _logger;

	private static IResourceManager _resourceManager;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PacketClientIsReadyHandler");
		_resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection)
	{
		_logger.LogInformation("PacketClientIsReady. Player={player}", connection.Player?.Guid ?? 0);
		connection.Player.Zone.OnClientIsReady(connection.Player);
		PacketReadyActivityDefinitions.SendStartupDefinitionsIfEnabled(connection, _logger);
		return true;
	}
}
