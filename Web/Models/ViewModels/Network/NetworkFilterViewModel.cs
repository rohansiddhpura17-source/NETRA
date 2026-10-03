using NETRA.Domain.Enums;

namespace NETRA.Web.Models.ViewModels.Network;

/// <summary>GET-bound filters for the server-rendered Network &amp; Link Analysis page.</summary>
public sealed class NetworkFilterViewModel
{
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string? Search { get; set; }

    public RelationshipType? RelationshipType { get; set; }

    public int? Depth { get; set; }

    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }
}
