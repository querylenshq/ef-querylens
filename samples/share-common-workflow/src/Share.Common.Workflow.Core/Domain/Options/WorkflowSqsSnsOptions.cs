using System.ComponentModel.DataAnnotations;

namespace Share.Common.Workflow.Core.Domain.Options;

public class WorkflowSqsSnsOptions
{
    [Required(AllowEmptyStrings = false)]
    public AccountDetailsSqsSnsOptions AccountDetails { get; set; } = new();

    public class AccountDetailsSqsSnsOptions
    {
        [Required(AllowEmptyStrings = false)]
        public string AccountDetailsQueue { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false)]
        public string AccountDetailsTopic { get; set; } = string.Empty;
    }
}
