using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.FakeMessageExecutors
{
    /// <summary>
    /// Records the request and creates an asyncoperation for it. A workflow registered through
    /// <see cref="XrmFakedContext.RegisterWorkflow"/> runs immediately; like the asynchronous server job,
    /// a failure marks the asyncoperation as failed instead of throwing to the caller.
    /// </summary>
    public class ExecuteWorkflowRequestExecutor : IFakeMessageExecutor
    {
        private const int WorkflowOperationType = 10;
        private const int ReadyState = 0;
        private const int CompletedState = 3;
        private const int WaitingForResourcesStatus = 0;
        private const int SucceededStatus = 30;
        private const int FailedStatus = 31;

        public bool CanExecute(OrganizationRequest request)
        {
            return request is ExecuteWorkflowRequest;
        }

        public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
        {
            var req = (ExecuteWorkflowRequest)request;

            if (req.WorkflowId == Guid.Empty)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "WorkflowId is required.");
            }

            if (req.EntityId == Guid.Empty)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "EntityId is required.");
            }

            ctx.RecordExecutedWorkflow(req);

            var asyncOperation = new Entity("asyncoperation")
            {
                Id = Guid.NewGuid(),
                ["operationtype"] = new OptionSetValue(WorkflowOperationType),
                ["workflowactivationid"] = new EntityReference("workflow", req.WorkflowId),
                ["statecode"] = new OptionSetValue(ReadyState),
                ["statuscode"] = new OptionSetValue(WaitingForResourcesStatus)
            };

            var run = ctx.GetWorkflowHandler(req.WorkflowId);
            if (run != null)
            {
                try
                {
                    run(ctx.GetOrganizationService(), req);
                    asyncOperation["statuscode"] = new OptionSetValue(SucceededStatus);
                }
                catch (Exception ex)
                {
                    asyncOperation["statuscode"] = new OptionSetValue(FailedStatus);
                    asyncOperation["message"] = ex.Message;
                }

                asyncOperation["statecode"] = new OptionSetValue(CompletedState);
                asyncOperation["completedon"] = DateTime.UtcNow;
            }

            ctx.AddEntityWithDefaults(asyncOperation);

            return new ExecuteWorkflowResponse
            {
                Results = new ParameterCollection { { "Id", asyncOperation.Id } }
            };
        }

        public Type GetResponsibleRequestType()
        {
            return typeof(ExecuteWorkflowRequest);
        }
    }
}
