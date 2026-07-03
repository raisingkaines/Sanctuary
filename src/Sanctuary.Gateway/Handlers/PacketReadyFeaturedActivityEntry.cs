using Sanctuary.Core.IO;

namespace Sanctuary.Gateway.Handlers;

internal sealed class PacketReadyFeaturedActivityEntry : ISerializableType
{
	public long StartEpoch { get; set; }

	public long StopEpoch { get; set; }

	public int BonusRewardSetId { get; set; }

	public bool UsingServerTime { get; set; }

	public int ScheduledId { get; set; }

	public int BonusRewardTooltipStringId { get; set; }

	public void Serialize(PacketWriter writer)
	{
		writer.Write(StartEpoch);
		writer.Write(StopEpoch);
		writer.Write(BonusRewardSetId);
		writer.Write(UsingServerTime);
		writer.Write(ScheduledId);
		writer.Write(BonusRewardTooltipStringId);
	}
}
