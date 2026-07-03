using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;

namespace Sanctuary.Gateway.Handlers;

internal static class PacketReadyActivityDefinitions
{
	private const string ExperimentFlagName = "TCG_USE_PACKET_READY_ACTIVITY_DEFINITIONS";

	private const int ActivityListUpdateMode = 1;

	private static readonly object LoadLock = new object();

	private static List<PacketReadyActivityDefinitionRow> _rows = new List<PacketReadyActivityDefinitionRow>();

	private static string _sourcePath = string.Empty;

	private static DateTime _sourceWriteUtc = DateTime.MinValue;

	public static bool IsStartupListEnabled()
	{
		if (TryParseBoolean(Environment.GetEnvironmentVariable("TCG_USE_PACKET_READY_ACTIVITY_DEFINITIONS"), out var result))
		{
			return result;
		}
		string path = Path.Combine(AppContext.BaseDirectory, "gateway.json");
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			if (jsonDocument.RootElement.TryGetProperty("TCG_USE_PACKET_READY_ACTIVITY_DEFINITIONS", out var value))
			{
				if (value.ValueKind == JsonValueKind.True)
				{
					return true;
				}
				if (value.ValueKind == JsonValueKind.False)
				{
					return false;
				}
				if (value.ValueKind == JsonValueKind.String && TryParseBoolean(value.GetString(), out result))
				{
					return result;
				}
			}
		}
		catch
		{
			return false;
		}
		return false;
	}

	public static bool TrySuppressOldTcgOverride(string sender, ulong playerGuid, bool worldTunnel, string reason, string source, int categoryId, string suppressedRows)
	{
		if (!IsStartupListEnabled())
		{
			return false;
		}
		MinigameDiagnosticsLog.Info("OldTcgOverrideSuppressed", "TcgDetailUiPatch", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("sender", sender), MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("suppressionReason", "packet-ready-test"), MinigameDiagnosticsLog.Field("source", source), MinigameDiagnosticsLog.Field("category", categoryId), MinigameDiagnosticsLog.Field("categoryId", categoryId), MinigameDiagnosticsLog.Field("suppressedRows", suppressedRows), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		return true;
	}

	public static void SendStartupDefinitionsIfEnabled(GatewayConnection connection, ILogger logger)
	{
		ulong playerGuid = connection.Player?.Guid ?? 0;
		List<PacketReadyActivityDefinitionRow> rows;
		string sourcePath;
		if (!IsStartupListEnabled())
		{
			MinigameDiagnosticsLog.Info("PacketReadyActivityDefinitionsSkipped", "PacketReadyActivityDefinitions", playerGuid, false, MinigameDiagnosticsLog.Field("reason", "experiment-flag-disabled"), MinigameDiagnosticsLog.Field("flag", "TCG_USE_PACKET_READY_ACTIVITY_DEFINITIONS"));
		}
		else if (TryLoadRows(playerGuid, worldTunnel: false, logger, out rows, out sourcePath))
		{
			SendStartupDefinitions(connection, rows, 2, sourcePath, playerGuid, logger);
			SendStartupDefinitions(connection, rows, 1, sourcePath, playerGuid, logger);
		}
	}

	public static bool TrySendCategoryDetail(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int requestedCategoryId, string reason)
	{
		if (!TryLoadRows(playerGuid, worldTunnel, null, out var rows, out var sourcePath))
		{
			return false;
		}
		if (!TryResolveCategoryRows(rows, requestedCategoryId, out var activityCategoryId, out var rows2, out var primaryRow, out var resolution))
		{
			MinigameDiagnosticsLog.Warn("GenericMinigameCategoryDataMissing", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId), MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("didShowNoGameSelected", true));
			return false;
		}
		SendCategoryDetailCore(connection, worldTunnel, playerGuid, reason, requestedCategoryId, activityCategoryId, rows2, primaryRow, resolution, sourcePath);
		if (worldTunnel)
		{
			SendCategoryDetailCore(connection, worldTunnel: false, playerGuid, reason + "-client-tunnel-mirror", requestedCategoryId, activityCategoryId, rows2, primaryRow, resolution, sourcePath);
		}
		return true;
	}

	public static bool TryShowCategoryDetailFromStartupList(GatewayConnection connection, bool worldTunnel, ulong playerGuid, int requestedCategoryId, string reason)
	{
		if (!TryLoadRows(playerGuid, worldTunnel, null, out var rows, out var sourcePath))
		{
			return false;
		}
		if (!TryResolveCategoryRows(rows, requestedCategoryId, out var activityCategoryId, out var rows2, out var primaryRow, out var resolution))
		{
			MinigameDiagnosticsLog.Warn("PacketReadyStartupCategoryDataMissing", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId), MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("didShowNoGameSelected", true));
			return false;
		}
		MinigameDiagnosticsLog.Info("PacketReadyStartupCategoryDetailUsed", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("resolution", resolution), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("rows", rows2.Count), MinigameDiagnosticsLog.Field("rowIds", BuildRowIdList(rows2)), MinigameDiagnosticsLog.Field("selectedGame", DescribeDisplayName(primaryRow)), MinigameDiagnosticsLog.Field("selectedId", primaryRow.Id), MinigameDiagnosticsLog.Field("activityId", primaryRow.Id), MinigameDiagnosticsLog.Field("miniGameId", primaryRow.Unknown3), MinigameDiagnosticsLog.Field("imageSetId", primaryRow.ImageSetId), MinigameDiagnosticsLog.Field("detailImage", primaryRow.DetailImageFilename), MinigameDiagnosticsLog.Field("thumbnailImage", primaryRow.ThumbnailImageFilename), MinigameDiagnosticsLog.Field("startupListOnly", true), MinigameDiagnosticsLog.Field("opcode167SentAfterStartup", false), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false));
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-show-from-startup-list", "MinigameDetail:Show", activityCategoryId);
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-populate-from-startup-list", "MinigameDetail:Populate", activityCategoryId);
		LogGenericDetailSelected(playerGuid, worldTunnel, reason, requestedCategoryId, activityCategoryId, primaryRow, rows2.Count, resolution);
		if (worldTunnel)
		{
			SendUiIntScript(connection, false, playerGuid, reason + "-client-tunnel-mirror-show-from-startup-list", "MinigameDetail:Show", activityCategoryId);
			SendUiIntScript(connection, false, playerGuid, reason + "-client-tunnel-mirror-populate-from-startup-list", "MinigameDetail:Populate", activityCategoryId);
			LogGenericDetailSelected(playerGuid, worldTunnel: false, reason + "-client-tunnel-mirror", requestedCategoryId, activityCategoryId, primaryRow, rows2.Count, resolution);
		}
		return true;
	}

	public static bool TryGetRowByActivityId(int activityId, ulong playerGuid, bool worldTunnel, out PacketReadyActivityDefinitionRow row)
	{
		row = null;
		if (!TryLoadRows(playerGuid, worldTunnel, null, out var rows, out var _))
		{
			return false;
		}
		row = rows.FirstOrDefault((PacketReadyActivityDefinitionRow candidate) => candidate.ServerType == 2 && candidate.Id == activityId);
		if (row == null)
		{
			row = rows.FirstOrDefault((PacketReadyActivityDefinitionRow candidate) => candidate.ServerType == 2 && candidate.Unknown3 == activityId);
		}
		if (row == null)
		{
			row = rows.FirstOrDefault((PacketReadyActivityDefinitionRow candidate) => candidate.Id == activityId);
		}
		if (row == null)
		{
			row = rows.FirstOrDefault((PacketReadyActivityDefinitionRow candidate) => candidate.Unknown3 == activityId);
		}
		return row != null;
	}

	public static bool TryGetPrimaryCategoryRow(int requestedCategoryId, ulong playerGuid, bool worldTunnel, out int activityCategoryId, out PacketReadyActivityDefinitionRow row)
	{
		activityCategoryId = requestedCategoryId;
		row = null;
		if (!TryLoadRows(playerGuid, worldTunnel, null, out var rows, out var sourcePath))
		{
			return false;
		}
		if (!TryResolveCategoryRows(rows, requestedCategoryId, out activityCategoryId, out var rows2, out row, out sourcePath))
		{
			return false;
		}
		if (row != null)
		{
			return rows2.Count > 0;
		}
		return false;
	}

	private static void SendStartupDefinitions(GatewayConnection connection, List<PacketReadyActivityDefinitionRow> rows, int serverType, string sourcePath, ulong playerGuid, ILogger logger)
	{
		List<PacketReadyActivityDefinitionRow> list = rows.Where((PacketReadyActivityDefinitionRow row) => row.ServerType == serverType).ToList();
		string value = BuildFirstRowIdList(list);
		bool flag = list.Any((PacketReadyActivityDefinitionRow row) => row.Id == 7);
		bool flag2 = list.Any(IsTreasureWarsRow);
		bool flag3 = list.Any((PacketReadyActivityDefinitionRow row) => row.Category == 11);
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)167);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)1);
		packetWriter.Write(serverType);
		packetWriter.Write(list);
		MinigameDiagnosticsLog.Info("PacketReadyActivityDefinitionsSent", "PacketReadyActivityDefinitions", playerGuid, false, MinigameDiagnosticsLog.Field("phase", "before-send"), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("serverType", serverType), MinigameDiagnosticsLog.Field("rowCount", list.Count), MinigameDiagnosticsLog.Field("firstRowIds", value), MinigameDiagnosticsLog.Field("row7Exists", flag), MinigameDiagnosticsLog.Field("treasureWarRowExists", flag2), MinigameDiagnosticsLog.Field("category11Exists", flag3), MinigameDiagnosticsLog.Field("tcgRow", DescribeRow(list.FirstOrDefault((PacketReadyActivityDefinitionRow row) => row.Id == 7))), MinigameDiagnosticsLog.Field("treasureWarsRow", DescribeRow(list.FirstOrDefault(IsTreasureWarsRow))), MinigameDiagnosticsLog.Field("bytes", packetWriter.Buffer.Length));
		TcgTunnelSend.Send(connection, worldTunnel: false, packetWriter.Buffer);
		logger?.LogInformation("Sent packet-ready ClientActivityDefinitions. ServerType={serverType}, Rows={rows}, Bytes={bytes}, Player={player}", serverType, list.Count, packetWriter.Buffer.Length, playerGuid);
		MinigameDiagnosticsLog.Info("PacketReadyActivityDefinitionsSent", "PacketReadyActivityDefinitions", playerGuid, false, MinigameDiagnosticsLog.Field("phase", "after-send"), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("serverType", serverType), MinigameDiagnosticsLog.Field("rowCount", list.Count), MinigameDiagnosticsLog.Field("firstRowIds", value), MinigameDiagnosticsLog.Field("row7Exists", flag), MinigameDiagnosticsLog.Field("treasureWarRowExists", flag2), MinigameDiagnosticsLog.Field("category11Exists", flag3), MinigameDiagnosticsLog.Field("tcgRow", DescribeRow(list.FirstOrDefault((PacketReadyActivityDefinitionRow row) => row.Id == 7))), MinigameDiagnosticsLog.Field("treasureWarsRow", DescribeRow(list.FirstOrDefault(IsTreasureWarsRow))), MinigameDiagnosticsLog.Field("bytes", packetWriter.Buffer.Length));
	}

	private static void SendCategoryDetailCore(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, int requestedCategoryId, int activityCategoryId, List<PacketReadyActivityDefinitionRow> rows, PacketReadyActivityDefinitionRow primaryRow, string resolution, string sourcePath)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)167);
		packetWriter.Write((byte)1);
		packetWriter.Write((byte)1);
		packetWriter.Write(1);
		packetWriter.Write(rows.Count);
		for (int i = 0; i < rows.Count; i++)
		{
			rows[i].Serialize(packetWriter);
		}
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("GenericMinigameCategoryDataSent", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId), MinigameDiagnosticsLog.Field("categoryId", activityCategoryId), MinigameDiagnosticsLog.Field("resolution", resolution), MinigameDiagnosticsLog.Field("source", sourcePath), MinigameDiagnosticsLog.Field("rows", rows.Count), MinigameDiagnosticsLog.Field("rowIds", BuildRowIdList(rows)), MinigameDiagnosticsLog.Field("selectedGame", DescribeDisplayName(primaryRow)), MinigameDiagnosticsLog.Field("selectedId", primaryRow.Id), MinigameDiagnosticsLog.Field("activityId", primaryRow.Id), MinigameDiagnosticsLog.Field("miniGameId", primaryRow.Unknown3), MinigameDiagnosticsLog.Field("imageSetId", primaryRow.ImageSetId), MinigameDiagnosticsLog.Field("detailImage", primaryRow.DetailImageFilename), MinigameDiagnosticsLog.Field("thumbnailImage", primaryRow.ThumbnailImageFilename), MinigameDiagnosticsLog.Field("activityListHeaderOrder", "opcode167,activityFamily=1,subtype=1,updateMode,rowCount"), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.Field("didShowNoGameSelected", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-show-after-datasource", "MinigameDetail:Show", activityCategoryId);
		SendUiIntScript(connection, worldTunnel, playerGuid, reason + "-populate-after-datasource", "MinigameDetail:Populate", activityCategoryId);
		LogGenericDetailSelected(playerGuid, worldTunnel, reason, requestedCategoryId, activityCategoryId, primaryRow, rows.Count, resolution);
	}

	private static void SendUiIntScript(GatewayConnection connection, bool worldTunnel, ulong playerGuid, string reason, string script, params int[] args)
	{
		using PacketWriter packetWriter = new PacketWriter();
		packetWriter.Write((short)47);
		packetWriter.Write((byte)7);
		packetWriter.Write(script);
		packetWriter.Write(args);
		TcgTunnelSend.Send(connection, worldTunnel, packetWriter.Buffer);
		MinigameDiagnosticsLog.Info("GenericMinigameUiPacketSent", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("reason", reason), MinigameDiagnosticsLog.Field("script", script), MinigameDiagnosticsLog.Field("params", string.Join(",", args)), MinigameDiagnosticsLog.Field("didLaunch", false), MinigameDiagnosticsLog.Field("didSendInvite", false), MinigameDiagnosticsLog.HexField("payload", packetWriter.Buffer));
	}

	private static void LogGenericDetailSelected(ulong playerGuid, bool worldTunnel, string reason, int requestedCategoryId, int activityCategoryId, PacketReadyActivityDefinitionRow primaryRow, int rowCount, string resolution)
	{
		string value = DescribeDisplayName(primaryRow);
		string[] fields = new string[17]
		{
			MinigameDiagnosticsLog.Field("reason", reason),
			MinigameDiagnosticsLog.Field("selectedGame", value),
			MinigameDiagnosticsLog.Field("selectedId", primaryRow.Id),
			MinigameDiagnosticsLog.Field("activityId", primaryRow.Id),
			MinigameDiagnosticsLog.Field("gameKey", "activity-category-" + activityCategoryId),
			MinigameDiagnosticsLog.Field("gameName", value),
			MinigameDiagnosticsLog.Field("categoryId", activityCategoryId),
			MinigameDiagnosticsLog.Field("requestedCategoryId", requestedCategoryId),
			MinigameDiagnosticsLog.Field("nativeMiniGameTypeId", primaryRow.Category),
			MinigameDiagnosticsLog.Field("miniGameId", primaryRow.Unknown3),
			MinigameDiagnosticsLog.Field("handler", "PacketReadyActivityDefinitions"),
			MinigameDiagnosticsLog.Field("sourceEvent", "MinigameDetailPopulate"),
			MinigameDiagnosticsLog.Field("resolution", resolution),
			MinigameDiagnosticsLog.Field("rows", rowCount),
			MinigameDiagnosticsLog.Field("didLaunch", false),
			MinigameDiagnosticsLog.Field("didSendInvite", false),
			MinigameDiagnosticsLog.Field("didShowNoGameSelected", false)
		};
		MinigameDiagnosticsLog.Info("GameSelected", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, fields);
		MinigameDiagnosticsLog.Info("GameDetailPanelUpdated", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, fields);
		MinigameDiagnosticsLog.Info("GenericMinigameDetailPopulateSent", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, fields);
	}

	private static bool TryLoadRows(ulong playerGuid, bool worldTunnel, ILogger logger, out List<PacketReadyActivityDefinitionRow> rows, out string sourcePath)
	{
		sourcePath = Path.Combine(AppContext.BaseDirectory, "Resources", "ClientActivityDefinitions.json");
		rows = null;
		if (!File.Exists(sourcePath))
		{
			logger?.LogWarning("ClientActivityDefinitions.json not found. Path={path}, Player={player}", sourcePath, playerGuid);
			MinigameDiagnosticsLog.Warn("ClientActivityDefinitionsMissing", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("path", sourcePath));
			return false;
		}
		DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
		lock (LoadLock)
		{
			if (_rows.Count > 0 && string.Equals(_sourcePath, sourcePath, StringComparison.OrdinalIgnoreCase) && _sourceWriteUtc == lastWriteTimeUtc)
			{
				rows = _rows;
				return true;
			}
			try
			{
				_rows = JsonSerializer.Deserialize<List<PacketReadyActivityDefinitionRow>>(File.ReadAllText(sourcePath), new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				}) ?? new List<PacketReadyActivityDefinitionRow>();
				_sourcePath = sourcePath;
				_sourceWriteUtc = lastWriteTimeUtc;
				rows = _rows;
				MinigameDiagnosticsLog.Info("ClientActivityDefinitionsLoaded", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("path", sourcePath), MinigameDiagnosticsLog.Field("rows", rows.Count), MinigameDiagnosticsLog.Field("serverType2Rows", rows.Count((PacketReadyActivityDefinitionRow row) => row.ServerType == 2)), MinigameDiagnosticsLog.Field("serverType1Rows", rows.Count((PacketReadyActivityDefinitionRow row) => row.ServerType == 1)));
				return true;
			}
			catch (Exception ex)
			{
				logger?.LogError(ex, "Failed to load ClientActivityDefinitions.json. Path={path}, Player={player}", sourcePath, playerGuid);
				MinigameDiagnosticsLog.Warn("ClientActivityDefinitionsLoadFailed", "PacketReadyActivityDefinitions", playerGuid, worldTunnel, MinigameDiagnosticsLog.Field("path", sourcePath), MinigameDiagnosticsLog.Field("error", ex.Message));
				return false;
			}
		}
	}

	private static bool TryResolveCategoryRows(List<PacketReadyActivityDefinitionRow> allRows, int requestedCategoryId, out int activityCategoryId, out List<PacketReadyActivityDefinitionRow> rows, out PacketReadyActivityDefinitionRow primaryRow, out string resolution)
	{
		activityCategoryId = requestedCategoryId;
		rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.ServerType == 2 && row.Category == requestedCategoryId).ToList();
		resolution = "category-serverType2";
		if (rows.Count == 0)
		{
			rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.Category == requestedCategoryId).ToList();
			resolution = "category-any-serverType";
		}
		if (rows.Count == 0)
		{
			PacketReadyActivityDefinitionRow rowMatch = allRows.FirstOrDefault((PacketReadyActivityDefinitionRow row) => row.Id == requestedCategoryId);
			if (rowMatch != null)
			{
				int matchedCategoryId = rowMatch.Category;
				activityCategoryId = matchedCategoryId;
				rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.ServerType == rowMatch.ServerType && row.Category == matchedCategoryId).ToList();
				if (rows.Count == 0)
				{
					rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.Category == matchedCategoryId).ToList();
				}
				resolution = "activity-id-to-category";
			}
		}
		if (rows.Count == 0)
		{
			PacketReadyActivityDefinitionRow unknownMatch = allRows.FirstOrDefault((PacketReadyActivityDefinitionRow row) => row.Unknown3 == requestedCategoryId);
			if (unknownMatch != null)
			{
				int matchedCategoryId2 = unknownMatch.Category;
				activityCategoryId = matchedCategoryId2;
				rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.ServerType == unknownMatch.ServerType && row.Category == matchedCategoryId2).ToList();
				if (rows.Count == 0)
				{
					rows = allRows.Where((PacketReadyActivityDefinitionRow row) => row.Category == matchedCategoryId2).ToList();
				}
				resolution = "backing-id-to-category";
			}
		}
		rows = (from row in rows
			orderby (row.ImagePositionId != 0) ? row.ImagePositionId : int.MaxValue, row.Id
			select row).ToList();
		primaryRow = rows.FirstOrDefault();
		if (rows.Count > 0)
		{
			return primaryRow != null;
		}
		return false;
	}

	private static bool TryParseBoolean(string value, out bool result)
	{
		result = false;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
		{
			result = true;
			return true;
		}
		if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
		{
			result = false;
			return true;
		}
		return false;
	}

	private static string BuildFirstRowIdList(List<PacketReadyActivityDefinitionRow> rows)
	{
		return string.Join(",", from row in rows.Take(8)
			select row.Id.ToString());
	}

	private static string BuildRowIdList(List<PacketReadyActivityDefinitionRow> rows)
	{
		return string.Join(",", rows.Select((PacketReadyActivityDefinitionRow row) => row.Id.ToString()));
	}

	private static bool IsTreasureWarsRow(PacketReadyActivityDefinitionRow row)
	{
		if (row != null)
		{
			if (row.Id != 1078 && row.Id != 39 && row.NameId != 434188)
			{
				return row.DisplayNameId == 434188;
			}
			return true;
		}
		return false;
	}

	private static string DescribeDisplayName(PacketReadyActivityDefinitionRow row)
	{
		if (row == null)
		{
			return string.Empty;
		}
		return "NameId:" + row.NameId;
	}

	private static string DescribeRow(PacketReadyActivityDefinitionRow row)
	{
		if (row == null)
		{
			return "missing";
		}
		return $"id={row.Id},category={row.Category},serverType={row.ServerType},imageSetId={row.ImageSetId},displayNameId={row.DisplayNameId},displayDescriptionId={row.DisplayDescriptionId},nameId={row.NameId},descriptionId={row.DescriptionId},detail={row.DetailImageFilename},thumb={row.ThumbnailImageFilename},unknown3={row.Unknown3}";
	}
}
