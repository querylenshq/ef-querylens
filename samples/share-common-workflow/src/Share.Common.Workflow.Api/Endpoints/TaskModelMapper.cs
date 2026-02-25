using Riok.Mapperly.Abstractions;
using Share.Common.Workflow.Api.Endpoints.Applications;
using Share.Common.Workflow.Core.Application.Services;

namespace Share.Common.Workflow.Api.Endpoints;

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.None,
    EnabledConversions = MappingConversionType.All & ~MappingConversionType.ImplicitCast
)]
public static partial class TaskModelMapper
{
    public static partial PostWorkflowStageDecisionTaskModel ToTaskModel(
        this PostWorkflowStageDecisionRequest request
    );
}
