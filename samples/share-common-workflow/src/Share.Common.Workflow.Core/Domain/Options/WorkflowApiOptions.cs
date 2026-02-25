using System.ComponentModel.DataAnnotations;

namespace Share.Common.Workflow.Core.Domain.Options;

public class WorkflowApiOptions
{
    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; set; } = default!;

    [Required(AllowEmptyStrings = false)]
    public string AppCode { get; set; } = default!;

    [Required(AllowEmptyStrings = false)]
    public string AppSecret { get; set; } = default!;
}
