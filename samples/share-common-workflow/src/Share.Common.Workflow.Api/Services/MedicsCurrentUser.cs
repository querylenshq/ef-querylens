using Microsoft.Extensions.Options;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Domain.Extensions;
using Share.Lib.Abstractions.Api;
using Share.Lib.Abstractions.Common.Services;

namespace Share.Common.Workflow.Api.Services;

public class MedicsCurrentUser(IHttpContextAccessor accessor, IOptions<CurrentUserOptions> options)
    : ApiCurrentUserService(accessor, options),
        IMedicsCurrentUser
{
    private readonly IHttpContextAccessor _accessor = accessor;

    public List<Enums.WorkflowRole> Roles
    {
        get
        {
            var httpContext = _accessor.HttpContext;

            if (httpContext == null)
            {
                return [];
            }

            var roles = httpContext
                .User.FindAll(ShareHttpParams.CommonClaims.Role)
                .Select(x => x.Value)
                .Distinct()
                .ToList();
            var medicsRoles = roles
                .GetRoleClaims()
                .Select(s => s.WorkflowRoleType)
                .Distinct()
                .ToList();

            return medicsRoles;
        }
    }
}

public interface IMedicsCurrentUser
{
    Guid UserAccountId { get; }
    List<Enums.WorkflowRole> Roles { get; }
}
