using System.ComponentModel.DataAnnotations;
using NETRA.Application.ImageInvestigation;

namespace NETRA.Web.Models.ViewModels.ImageInvestigation;

/// <summary>Upload form. Domain entities are never bound directly.</summary>
public sealed class ImageUploadViewModel
{
    [Display(Name = "Case")]
    public Guid? CrimeCaseId { get; set; }

    [Display(Name = "Investigation image")]
    public IFormFile? File { get; set; }
}

public sealed class ImageInvestigationViewModel
{
    public required IReadOnlyList<ImageCaseOptionDto> Cases { get; init; }

    public ImageInvestigationResultDto? Result { get; init; }

    public ImageUploadViewModel Upload { get; init; } = new();

    public static ImageInvestigationViewModel Create(
        IReadOnlyList<ImageCaseOptionDto> cases,
        ImageInvestigationResultDto? result,
        Guid? selectedCaseId = null) => new()
        {
            Cases = cases,
            Result = result,
            Upload = new ImageUploadViewModel { CrimeCaseId = selectedCaseId ?? result?.CrimeCaseId }
        };
}

public sealed class ImageMatchDetailsViewModel
{
    public required ImageMatchDetailDto Detail { get; init; }
}
