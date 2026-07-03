using System;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class OneTimeSessionPacketHandler
{
	private const int NativeTcgMaxStartupNameLength = 16;

	private static ILogger _logger;

	public static void ConfigureServices(IServiceProvider serviceProvider)
	{
		_logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OneTimeSessionPacketHandler");
		MinigameDiagnosticsLog.ConfigureServices(serviceProvider);
	}

	public static bool HandlePacket(GatewayConnection connection, PacketReader reader, bool worldTunnel)
	{
		int result = 0;
		reader.TryRead(out result);
		ulong num = connection.Player?.Guid ?? 0;
		string text = TcgSessionRegistry.Register(num, result);
		MinigameDiagnosticsLog.Info("one-time-session-request", "OneTimeSessionPacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("sessionType", result), MinigameDiagnosticsLog.Field("ticket", text), MinigameDiagnosticsLog.Field("isTcg", result == 41));
		SendSessionResponse(connection, worldTunnel, num, result, text);
		if (result == 41)
		{
			TcgMatchmakingState.JoinQueue(num, 41);
			MinigameDiagnosticsLog.Info("one-time-session-tcg-launch", "OneTimeSessionPacketHandler", num, worldTunnel, MinigameDiagnosticsLog.Field("queue", 41), MinigameDiagnosticsLog.Field("ticket", text));
			ClientActivityLaunchPacketHandler.CompleteTcgLaunch(connection, worldTunnel, num, text, sendSessionTicket: false);
		}
		_logger.LogInformation("OneTimeSessionResponse. Player={player}, Type={type}, Ticket={ticket}", num, result, text);
		return true;
	}

	public static void SendSessionResponse(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int sessionType, string ticket, string eventName = "one-time-session-response")
	{
		string stationName = GetStationName(connection, playerGuid);
		string reason = string.Empty;
		string text = ((sessionType == 41) ? BuildNativeTcgStartupName(stationName, out reason) : stationName);
		string text2 = ((sessionType == 41) ? QuoteNativeTcgArgument(text) : text);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)118);
		packetWriter.Write((byte)1);
		packetWriter.Write(ticket ?? string.Empty);
		packetWriter.Write(sessionType);
		packetWriter.Write(text2);
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info(eventName, "OneTimeSessionPacketHandler", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sessionType", sessionType), MinigameDiagnosticsLog.Field("ticket", ticket ?? string.Empty), MinigameDiagnosticsLog.Field("stationName", stationName), MinigameDiagnosticsLog.Field("nativeStartupStationName", text), MinigameDiagnosticsLog.Field("nativeStartupStationNameArg", text2), MinigameDiagnosticsLog.Field("nativeStartupStationNameAliased", text != stationName), MinigameDiagnosticsLog.Field("nativeStartupStationNameAliasReason", reason ?? string.Empty), MinigameDiagnosticsLog.Field("nativeStartupStationNameQuoted", text2 != text), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static string GetStationName(GatewayConnection connection, ulong playerGuid)
	{
		if (connection.Player == null)
		{
			if (playerGuid != 0L)
			{
				return playerGuid.ToString();
			}
			return "STATION";
		}
		string obj = connection.Player.Name.FirstName ?? string.Empty;
		string text = connection.Player.Name.LastName ?? string.Empty;
		string text2 = (obj + " " + text).Trim();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return playerGuid.ToString();
	}

	private static string BuildNativeTcgStartupName(string value, out string reason)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length <= 16)
		{
			reason = "display-name-within-native-limit";
			return text;
		}
		string text2 = CompactNativeTcgName(text);
		if (!string.IsNullOrWhiteSpace(text2) && text2.Length <= 16)
		{
			reason = "whitespace-compacted-to-native-limit";
			return text2;
		}
		string text3 = FirstNativeTcgNameToken(text);
		if (!string.IsNullOrWhiteSpace(text3) && text3.Length <= 16)
		{
			reason = "first-token-fallback";
			return text3;
		}
		reason = "hard-truncated-to-native-limit";
		return text.Substring(0, Math.Min(text.Length, 16));
	}

	private static string CompactNativeTcgName(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (!char.IsWhiteSpace(c) && c != '"' && c != '\\')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string FirstNativeTcgNameToken(string value)
	{
		string[] array = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return value;
	}

	private static string QuoteNativeTcgArgument(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value ?? string.Empty;
		}
		if (!NeedsNativeTcgQuoting(value))
		{
			return value;
		}
		string text = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		return "\"" + text + "\"";
	}

	private static bool NeedsNativeTcgQuoting(string value)
	{
		foreach (char c in value)
		{
			if (char.IsWhiteSpace(c) || c == '"')
			{
				return true;
			}
		}
		return false;
	}
}
