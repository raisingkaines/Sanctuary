using System;

namespace Sanctuary.Gateway.Handlers;

internal static class MinigameTileEventReader
{
	private const int SelectedId = 727;

	private const int ActivityId = 24;

	private const int CategoryId = 11;

	private const int QueueId = 41;

	private static readonly short[] WatchedOpcodes = new short[10] { 39, 47, 102, 109, 117, 141, 167, 175, 180, 194 };

	public static void Incoming(GatewayConnection connection, bool worldTunnel, short opcode, ReadOnlySpan<byte> payload, string source)
	{
		Log("TcgTileNativeInbound", connection, worldTunnel, "client-to-server", opcode, payload, source);
	}

	public static void Outgoing(GatewayConnection connection, bool worldTunnel, short opcode, ReadOnlySpan<byte> payload, string source)
	{
		Log("TcgTileNativeOutbound", connection, worldTunnel, "server-to-client", opcode, payload, source);
	}

	public static void Decoded(GatewayConnection connection, bool worldTunnel, short opcode, string source, string family, int type, ReadOnlySpan<byte> remaining, params string[] fields)
	{
		if (ShouldLogOpcode(opcode))
		{
			MinigameDiagnosticsLog.Info("TcgTileNativeDecoded", source, connection.Player?.Guid ?? 0, worldTunnel, BaseFields("decoded", opcode, default(ReadOnlySpan<byte>), source, null, fields, MinigameDiagnosticsLog.Field("family", family), MinigameDiagnosticsLog.Field("type", type), MinigameDiagnosticsLog.HexField("remaining", remaining)));
		}
	}

	private static void Log(string eventName, GatewayConnection connection, bool worldTunnel, string direction, short opcode, ReadOnlySpan<byte> payload, string source)
	{
		if (ShouldLogOpcode(opcode))
		{
			MinigameDiagnosticsLog.Info(eventName, "MinigameTileEventReader", connection.Player?.Guid ?? 0, worldTunnel, BaseFields(direction, opcode, payload, source, TrySubTypeField(opcode, payload), null));
		}
	}

	private static bool ShouldLogOpcode(short opcode)
	{
		return Array.IndexOf(WatchedOpcodes, opcode) >= 0;
	}

	private static string[] BaseFields(string direction, short opcode, ReadOnlySpan<byte> payload, string source, string extraField, string[] extraFields, params string[] tailFields)
	{
		string[] array = new string[19]
		{
			MinigameDiagnosticsLog.Field("direction", direction),
			MinigameDiagnosticsLog.Field("opcode", opcode),
			MinigameDiagnosticsLog.Field("opcodeName", OpcodeName(opcode)),
			MinigameDiagnosticsLog.Field("sourceEvent", source),
			MinigameDiagnosticsLog.Field("selectedId", 727),
			MinigameDiagnosticsLog.Field("activityId", 24),
			MinigameDiagnosticsLog.Field("gameKey", "tcg"),
			MinigameDiagnosticsLog.Field("gameName", "Trading Card Game"),
			MinigameDiagnosticsLog.Field("categoryId", 11),
			MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", 23),
			MinigameDiagnosticsLog.Field("miniGameId", 727),
			MinigameDiagnosticsLog.Field("queue", 41),
			MinigameDiagnosticsLog.Field("handler", "PassiveTileReader"),
			MinigameDiagnosticsLog.Field("didLaunch", false),
			MinigameDiagnosticsLog.Field("didSendInvite", false),
			MinigameDiagnosticsLog.Field("didShowNoGameSelected", false),
			MinigameDiagnosticsLog.Field("passive", true),
			MinigameDiagnosticsLog.Field("payloadBytes", payload.Length),
			(payload.Length > 0) ? MinigameDiagnosticsLog.HexField("payload", payload) : string.Empty
		};
		int num = ((!string.IsNullOrWhiteSpace(extraField)) ? 1 : 0);
		int num2 = ((extraFields != null) ? extraFields.Length : 0);
		int num3 = ((tailFields != null) ? tailFields.Length : 0);
		string[] array2 = new string[array.Length + num + num2 + num3];
		Array.Copy(array, array2, array.Length);
		int num4 = array.Length;
		if (num == 1)
		{
			array2[num4++] = extraField;
		}
		if (num2 > 0)
		{
			Array.Copy(extraFields, 0, array2, num4, num2);
			num4 += num2;
		}
		if (num3 > 0)
		{
			Array.Copy(tailFields, 0, array2, num4, num3);
		}
		return array2;
	}

	private static string TrySubTypeField(short opcode, ReadOnlySpan<byte> payload)
	{
		try
		{
			if (payload.Length <= 2)
			{
				return string.Empty;
			}
			return opcode switch
			{
				39 => MinigameDiagnosticsLog.Field("subType", payload[2]), 
				47 => MinigameDiagnosticsLog.Field("subType", payload[2]), 
				141 => (payload.Length >= 4) ? MinigameDiagnosticsLog.Field("subType", BitConverter.ToInt16(payload.Slice(2, 2))) : string.Empty, 
				167 => (payload.Length >= 4) ? MinigameDiagnosticsLog.Fields(MinigameDiagnosticsLog.Field("activityFamily", payload[2]), MinigameDiagnosticsLog.Field("subType", payload[3])) : string.Empty, 
				175 => (payload.Length >= 6) ? MinigameDiagnosticsLog.Field("subType", BitConverter.ToInt32(payload.Slice(2, 4))) : string.Empty, 
				180 => (payload.Length >= 6) ? MinigameDiagnosticsLog.Field("subType", BitConverter.ToInt32(payload.Slice(2, 4))) : string.Empty, 
				194 => (payload.Length >= 3) ? MinigameDiagnosticsLog.Field("type", payload[2]) : string.Empty, 
				_ => string.Empty, 
			};
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string OpcodeName(short opcode)
	{
		return opcode switch
		{
			39 => "BaseMiniGame", 
			47 => "Ui", 
			102 => "BaseLobbyGameDefinition", 
			109 => "ClientLog", 
			117 => "OneTimeSession", 
			141 => "Matchmaking", 
			167 => "ActivityService", 
			175 => "ClientActivityLaunch", 
			180 => "InviteAndStartMiniGame", 
			194 => "WallOfData", 
			_ => "Unknown", 
		};
	}
}
