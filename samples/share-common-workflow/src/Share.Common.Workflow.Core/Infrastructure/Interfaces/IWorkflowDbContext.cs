using Share.Common.Workflow.Core.Entities;
using Share.Lib.Bootstrap.Api.Core.Infrastructure.Interfaces;

namespace Share.Common.Workflow.Core.Infrastructure.Interfaces;

public interface IWorkflowDbContext : IWorkflowEntities, IShareAuthDbContext;
