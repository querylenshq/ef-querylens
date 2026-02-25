using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;

namespace Share.Common.Workflow.Core.Application.Services;

public class WorkflowService(IWorkflowDbContext dbContext)
{
    public async Task<TResult?> GetWorkflowByIdAsync<TResult>(
        Guid workflowId,
        Expression<Func<Entities.Workflow, TResult>> expression,
        CancellationToken ct
    )
    {
        return await dbContext
            .Workflows.Where(w => w.IsNotDeleted)
            .Where(w => w.WorkflowId == workflowId)
            .Select(expression)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<TResult?> GetWorkflowByTypeAsync<TResult>(
        Enums.WorkflowType workflowType,
        Expression<Func<Entities.Workflow, TResult>> expression,
        CancellationToken ct
    )
    {
        return await dbContext
            .Workflows.Where(w => w.WorkflowType == workflowType && w.IsNotDeleted)
            .Select(expression)
            .SingleOrDefaultAsync(ct);
    }
}
