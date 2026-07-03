using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace Sanctuary.Gateway.Handlers;

internal static class TcgSessionRegistry
{
	private sealed class SessionEntry
	{
		public ulong PlayerGuid;

		public int SessionType;

		public ulong LaunchTicket;

		public DateTime CreatedUtc;
	}

	private static readonly ConcurrentDictionary<string, SessionEntry> Tickets = new ConcurrentDictionary<string, SessionEntry>();

	public const int TcgQueueId = 41;

	public const int TcgActivityId = 7;

	public const int TcgActivityCategoryId = 11;

	public const int TcgActivityContentId = 36;

	public const int TcgLobbyType = 7;

	public const int TcgActivityPortalType = 11;

	public const int TcgSanctuaryGameId = 39;

	public const int TcgSnowhillGameId = 40;

	public const int TcgNativeGameId = 387;

	public const int TcgNativeMiniGameType = 16;

	public const int TcgNativeMiniGameNameId = 1320287513;

	public const int TcgNativeMiniGameDescriptionId = 903298991;

	public const int TcgNativeMiniGameIconId = 7533;

	public const int TcgNativeMiniGameDifficulty = 1;

	public const int TcgEncounterIconId = 9869;

	public const int TcgNameId = 3388;

	public const int TcgSanctuaryGameNameId = 3388;

	public const int TcgSnowhillGameNameId = 31328;

	public const int TcgPortalGameNameId = 1320287513;

	public const int TcgPortalGameDescriptionId = 903298991;

	public const int TcgPortalGameIconId = 9867;

	public const string TcgPortalGameName = "Trading Card Game";

	public const string TcgPortalGameDescription = "Play the Free Realms Trading Card Game.";

	public static bool IsTcgLaunchActivityId(int activityId)
	{
		if (activityId != 387 && activityId != 7 && activityId != 41 && activityId != 39 && activityId != 40 && activityId != 602 && activityId != 603)
		{
			return activityId == 604;
		}
		return true;
	}

	public static int ClearForStartup()
	{
		int count = Tickets.Count;
		Tickets.Clear();
		return count;
	}

	public static string Register(ulong playerGuid, int sessionType)
	{
		string text = $"{playerGuid}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds():x}";
		Tickets[text] = new SessionEntry
		{
			PlayerGuid = playerGuid,
			SessionType = sessionType,
			LaunchTicket = CreateLaunchTicket(playerGuid, text),
			CreatedUtc = DateTime.UtcNow
		};
		Tickets[Tickets[text].LaunchTicket.ToString(CultureInfo.InvariantCulture)] = Tickets[text];
		PruneExpired();
		return text;
	}

	public static ulong GetLaunchTicket(ulong playerGuid, string ticket)
	{
		if (!string.IsNullOrEmpty(ticket) && Tickets.TryGetValue(ticket, out var value) && value.PlayerGuid == playerGuid)
		{
			return value.LaunchTicket;
		}
		return CreateLaunchTicket(playerGuid, ticket ?? string.Empty);
	}

	public static int GetLaunchTicket32(ulong playerGuid, string ticket)
	{
		int num = (int)(GetLaunchTicket(playerGuid, ticket) & 0x7FFFFFFF);
		if (num != 0)
		{
			return num;
		}
		return 1;
	}

	public static bool TryValidate(string ticket, ulong playerGuid, int sessionType)
	{
		if (!Tickets.TryGetValue(ticket, out var value))
		{
			return false;
		}
		if (value.PlayerGuid != playerGuid || value.SessionType != sessionType)
		{
			return false;
		}
		return DateTime.UtcNow - value.CreatedUtc < TimeSpan.FromHours(2);
	}

	private static void PruneExpired()
	{
		DateTime dateTime = DateTime.UtcNow - TimeSpan.FromHours(4);
		foreach (KeyValuePair<string, SessionEntry> ticket in Tickets)
		{
			if (ticket.Value.CreatedUtc < dateTime)
			{
				Tickets.TryRemove(ticket.Key, out var _);
			}
		}
	}

	private static ulong CreateLaunchTicket(ulong playerGuid, string ticket)
	{
		ulong num = 0xCBF29CE484222325uL ^ playerGuid;
		foreach (char c in ticket)
		{
			num ^= c;
			num *= 1099511628211L;
		}
		if (num != 0L)
		{
			return num;
		}
		return 1uL;
	}
}
