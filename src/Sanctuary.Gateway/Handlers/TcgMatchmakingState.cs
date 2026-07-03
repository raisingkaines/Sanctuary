using System;
using System.Collections.Concurrent;

namespace Sanctuary.Gateway.Handlers;

internal static class TcgMatchmakingState
{
	private sealed class PlayerQueueState
	{
		public int QueueId = 41;

		public bool InQueue;

		public bool MatchReady;

		public int ListQueueRequests;

		public DateTime FirstListQueueRequestUtc = DateTime.MinValue;

		public DateTime LastLaunchUtc = DateTime.MinValue;

		public DateTime LastLaunchRefreshUtc = DateTime.MinValue;

		public string LastTicket = string.Empty;

		public bool LaunchFollowupsSent;

		public bool LaunchDeclined;

		public bool DetailDataSent;

		public DateTime LastTcgTileSelectionUtc = DateTime.MinValue;

		public int LastTcgTileSelectionCategoryId = 11;

		public DateTime LastTcgDetailPlaySelectionUtc = DateTime.MinValue;

		public int LastTcgDetailPlaySelectionRowId = 41;
	}

	public sealed class ListQueueObservation
	{
		public int Count { get; init; }

		public int Threshold { get; init; }

		public bool ShouldAutoLaunch { get; init; }

		public double WindowSeconds { get; init; }

		public double CooldownRemainingSeconds { get; init; }
	}

	private const int AutoLaunchListQueueThreshold = 2;

	private static readonly TimeSpan AutoLaunchWindow = TimeSpan.FromSeconds(8L);

	private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(30L);

	private static readonly TimeSpan LaunchRefreshInterval = TimeSpan.FromSeconds(6L);

	private static readonly TimeSpan RecentLaunchWindow = TimeSpan.FromMinutes(2.0);

	private static readonly TimeSpan RecentTcgTileSelectionWindow = TimeSpan.FromSeconds(6.0);

	private static readonly TimeSpan RecentTcgDetailPlaySelectionWindow = TimeSpan.FromSeconds(15.0);

	private static readonly object Sync = new object();

	private static readonly ConcurrentDictionary<ulong, PlayerQueueState> Players = new ConcurrentDictionary<ulong, PlayerQueueState>();

	public static int ClearForStartup()
	{
		lock (Sync)
		{
			int count = Players.Count;
			Players.Clear();
			return count;
		}
	}

	public static void JoinQueue(ulong playerGuid, int queueId)
	{
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			orAdd.QueueId = queueId;
			orAdd.InQueue = true;
			orAdd.MatchReady = true;
			orAdd.ListQueueRequests = 0;
			orAdd.FirstListQueueRequestUtc = DateTime.MinValue;
		}
	}

	public static void LeaveQueue(ulong playerGuid)
	{
		Players.TryRemove(playerGuid, out var _);
	}

	public static ListQueueObservation RecordListQueuesRequest(ulong playerGuid)
	{
		DateTime utcNow = DateTime.UtcNow;
		if (playerGuid == 0L)
		{
			return new ListQueueObservation
			{
				Count = 1,
				Threshold = 2,
				WindowSeconds = AutoLaunchWindow.TotalSeconds
			};
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			if (orAdd.FirstListQueueRequestUtc == DateTime.MinValue || utcNow - orAdd.FirstListQueueRequestUtc > AutoLaunchWindow)
			{
				orAdd.FirstListQueueRequestUtc = utcNow;
				orAdd.ListQueueRequests = 0;
			}
			orAdd.ListQueueRequests++;
			int listQueueRequests = orAdd.ListQueueRequests;
			TimeSpan timeSpan = ((orAdd.LastLaunchUtc == DateTime.MinValue) ? TimeSpan.MaxValue : (utcNow - orAdd.LastLaunchUtc));
			TimeSpan timeSpan2 = ((timeSpan >= LaunchCooldown) ? TimeSpan.Zero : (LaunchCooldown - timeSpan));
			return new ListQueueObservation
			{
				Count = listQueueRequests,
				Threshold = 2,
				ShouldAutoLaunch = false,
				WindowSeconds = AutoLaunchWindow.TotalSeconds,
				CooldownRemainingSeconds = Math.Ceiling(timeSpan2.TotalSeconds)
			};
		}
	}

	public static bool TryMarkDetailDataSent(ulong playerGuid)
	{
		if (playerGuid == 0L)
		{
			return false;
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			if (orAdd.DetailDataSent)
			{
				return false;
			}
			orAdd.DetailDataSent = true;
			return true;
		}
	}

	public static void MarkTcgTileSelected(ulong playerGuid, int activityCategoryId = 11)
	{
		if (playerGuid == 0L)
		{
			return;
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			orAdd.LastTcgTileSelectionUtc = DateTime.UtcNow;
			orAdd.LastTcgTileSelectionCategoryId = (TcgDetailUiPatch.IsTcgDetailCategory(activityCategoryId) ? activityCategoryId : 11);
		}
	}

	public static bool TryGetRecentTcgTileSelection(ulong playerGuid, out double selectionAgeMs)
	{
		int activityCategoryId;
		return TryGetRecentTcgTileSelection(playerGuid, out selectionAgeMs, out activityCategoryId);
	}

	public static bool TryGetRecentTcgTileSelection(ulong playerGuid, out double selectionAgeMs, out int activityCategoryId)
	{
		selectionAgeMs = -1.0;
		activityCategoryId = 11;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			if (!Players.TryGetValue(playerGuid, out var value) || value.LastTcgTileSelectionUtc == DateTime.MinValue)
			{
				return false;
			}
			TimeSpan timeSpan = utcNow - value.LastTcgTileSelectionUtc;
			selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
			if (timeSpan <= RecentTcgTileSelectionWindow)
			{
				activityCategoryId = value.LastTcgTileSelectionCategoryId;
				return true;
			}
			value.LastTcgTileSelectionUtc = DateTime.MinValue;
			return false;
		}
	}

	public static void MarkTcgDetailPlaySelected(ulong playerGuid, int selectedRowId)
	{
		if (playerGuid == 0L)
		{
			return;
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			orAdd.LastTcgDetailPlaySelectionUtc = DateTime.UtcNow;
			orAdd.LastTcgDetailPlaySelectionRowId = MiniGamePacketHandler.ResolveTradingCardStartScreenDisplayRowId(selectedRowId);
		}
	}

	public static bool TryGetRecentTcgDetailPlaySelection(ulong playerGuid, out int selectedRowId, out double selectionAgeMs)
	{
		DateTime selectedAtUtc;
		return TryGetRecentTcgDetailPlaySelection(playerGuid, out selectedRowId, out selectionAgeMs, out selectedAtUtc);
	}

	public static bool TryGetRecentTcgDetailPlaySelection(ulong playerGuid, out int selectedRowId, out double selectionAgeMs, out DateTime selectedAtUtc)
	{
		selectedRowId = 41;
		selectionAgeMs = -1.0;
		selectedAtUtc = DateTime.MinValue;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			if (!Players.TryGetValue(playerGuid, out var value) || value.LastTcgDetailPlaySelectionUtc == DateTime.MinValue)
			{
				return false;
			}
			TimeSpan timeSpan = utcNow - value.LastTcgDetailPlaySelectionUtc;
			selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
			if (timeSpan <= RecentTcgDetailPlaySelectionWindow)
			{
				selectedRowId = value.LastTcgDetailPlaySelectionRowId;
				selectedAtUtc = value.LastTcgDetailPlaySelectionUtc;
				return true;
			}
			value.LastTcgDetailPlaySelectionUtc = DateTime.MinValue;
			return false;
		}
	}

	public static bool TryGetTcgDetailPlaySelectionForDiagnostics(ulong playerGuid, out int selectedRowId, out double selectionAgeMs, out DateTime selectedAtUtc, out bool isFresh)
	{
		selectedRowId = 41;
		selectionAgeMs = -1.0;
		selectedAtUtc = DateTime.MinValue;
		isFresh = false;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			if (!Players.TryGetValue(playerGuid, out var value) || value.LastTcgDetailPlaySelectionUtc == DateTime.MinValue)
			{
				return false;
			}
			TimeSpan timeSpan = utcNow - value.LastTcgDetailPlaySelectionUtc;
			selectedRowId = value.LastTcgDetailPlaySelectionRowId;
			selectedAtUtc = value.LastTcgDetailPlaySelectionUtc;
			selectionAgeMs = Math.Round(timeSpan.TotalMilliseconds, 1);
			isFresh = timeSpan <= RecentTcgDetailPlaySelectionWindow;
			return true;
		}
	}

	public static bool TryBeginLaunch(ulong playerGuid, int queueId, out double cooldownRemainingSeconds)
	{
		cooldownRemainingSeconds = 0.0;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			TimeSpan timeSpan = ((orAdd.LastLaunchUtc == DateTime.MinValue) ? TimeSpan.MaxValue : (utcNow - orAdd.LastLaunchUtc));
			TimeSpan timeSpan2 = ((timeSpan >= LaunchCooldown) ? TimeSpan.Zero : (LaunchCooldown - timeSpan));
			cooldownRemainingSeconds = Math.Ceiling(timeSpan2.TotalSeconds);
			if (timeSpan2 > TimeSpan.Zero)
			{
				return false;
			}
			orAdd.QueueId = queueId;
			orAdd.InQueue = true;
			orAdd.MatchReady = true;
			return true;
		}
	}

	public static void MarkLaunchSent(ulong playerGuid, string ticket)
	{
		if (playerGuid == 0L)
		{
			return;
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			orAdd.LastLaunchUtc = DateTime.UtcNow;
			orAdd.LastLaunchRefreshUtc = DateTime.MinValue;
			orAdd.LastTicket = ticket ?? orAdd.LastTicket;
			orAdd.LaunchFollowupsSent = false;
			orAdd.LaunchDeclined = false;
			orAdd.ListQueueRequests = 0;
			orAdd.FirstListQueueRequestUtc = DateTime.MinValue;
		}
	}

	public static bool TryMarkLaunchFollowupsSent(ulong playerGuid, string ticket)
	{
		if (playerGuid == 0L)
		{
			return false;
		}
		lock (Sync)
		{
			PlayerQueueState orAdd = Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState());
			if (orAdd.LaunchDeclined)
			{
				return false;
			}
			if (orAdd.LaunchFollowupsSent && string.Equals(orAdd.LastTicket, ticket, StringComparison.Ordinal))
			{
				return false;
			}
			orAdd.LaunchFollowupsSent = true;
			orAdd.LastTicket = ticket ?? orAdd.LastTicket;
			return true;
		}
	}

	public static void MarkLaunchDeclined(ulong playerGuid)
	{
		if (playerGuid == 0L)
		{
			return;
		}
		lock (Sync)
		{
			Players.GetOrAdd(playerGuid, (ulong _) => new PlayerQueueState()).LaunchDeclined = true;
		}
	}

	public static bool TryGetRecentLaunchTicket(ulong playerGuid, out string ticket)
	{
		ticket = string.Empty;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			if (!Players.TryGetValue(playerGuid, out var value))
			{
				return false;
			}
			if (value.LastLaunchUtc == DateTime.MinValue || utcNow - value.LastLaunchUtc > RecentLaunchWindow)
			{
				return false;
			}
			ticket = value.LastTicket;
			return !string.IsNullOrEmpty(ticket);
		}
	}

	public static bool TryConsumeLaunchRefresh(ulong playerGuid, out string ticket)
	{
		ticket = string.Empty;
		if (playerGuid == 0L)
		{
			return false;
		}
		DateTime utcNow = DateTime.UtcNow;
		lock (Sync)
		{
			if (!Players.TryGetValue(playerGuid, out var value))
			{
				return false;
			}
			if (value.LastLaunchUtc == DateTime.MinValue || utcNow - value.LastLaunchUtc > RecentLaunchWindow)
			{
				return false;
			}
			if (value.LastLaunchRefreshUtc != DateTime.MinValue && utcNow - value.LastLaunchRefreshUtc < LaunchRefreshInterval)
			{
				return false;
			}
			value.LastLaunchRefreshUtc = utcNow;
			ticket = value.LastTicket;
			return true;
		}
	}
}
