using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sanctuary.Core.IO;

namespace Sanctuary.Gateway.Handlers;

internal sealed class PacketReadyActivityDefinitionRow : ISerializableType
{
	public int Id { get; set; }

	public int AppSystemId { get; set; }

	[JsonPropertyName("Unknown")]
	public int Unknown3 { get; set; }

	public int Category { get; set; }

	public int ImageSetId { get; set; }

	public int ImagePositionId { get; set; }

	public int ServerType { get; set; }

	public int DisplayNameId { get; set; }

	public int DisplayDescriptionId { get; set; }

	public int NameId { get; set; }

	public int DescriptionId { get; set; }

	public bool PlayerCanJoin { get; set; }

	public int PreferredRequirementId { get; set; }

	public int TutorialActivityId { get; set; }

	public bool MembersOnly { get; set; }

	public string DetailImageFilename { get; set; } = string.Empty;

	public string ThumbnailImageFilename { get; set; } = string.Empty;

	public int Difficulty { get; set; }

	public int MysteryChestIcon { get; set; }

	public int MysteryChestId { get; set; }

	public Dictionary<int, PacketReadyFeaturedActivityEntry> FeaturedActivities { get; set; } = new Dictionary<int, PacketReadyFeaturedActivityEntry>();

	public void Serialize(PacketWriter writer)
	{
		writer.Write(Id);
		writer.Write(AppSystemId);
		writer.Write(Category);
		writer.Write(ImageSetId);
		writer.Write(ImagePositionId);
		writer.Write(DisplayNameId);
		writer.Write(DisplayDescriptionId);
		writer.Write(NameId);
		writer.Write(DescriptionId);
		writer.Write(ServerType);
		writer.Write(PlayerCanJoin);
		writer.Write(PreferredRequirementId);
		writer.Write(FeaturedActivities);
		writer.Write(TutorialActivityId);
		writer.Write(MembersOnly);
		writer.Write(DetailImageFilename);
		writer.Write(ThumbnailImageFilename);
		writer.Write(Difficulty);
		writer.Write(Unknown3);
		writer.Write(MysteryChestId);
		writer.Write(MysteryChestIcon);
	}
}
