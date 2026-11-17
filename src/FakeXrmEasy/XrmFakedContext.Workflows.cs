using System;
using System.Collections.Generic;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy
{
    public partial class XrmFakedContext
    {
        private readonly Dictionary<Guid, Action<IOrganizationService, ExecuteWorkflowRequest>> _workflowHandlers =
            new Dictionary<Guid, Action<IOrganizationService, ExecuteWorkflowRequest>>();

        private readonly List<ExecuteWorkflowRequest> _executedWorkflowRequests = new List<ExecuteWorkflowRequest>();

        /// <summary>
        /// Every ExecuteWorkflowRequest executed against this context, in execution order.
        /// </summary>
        public IReadOnlyList<ExecuteWorkflowRequest> ExecutedWorkflowRequests => _executedWorkflowRequests;

        /// <summary>
        /// Registers what happens when the workflow with the given id is started through an ExecuteWorkflowRequest.
        /// Without a registration the request is only recorded.
        /// </summary>
        /// <param name="workflowId">The id of the workflow.</param>
        /// <param name="run">Simulates the workflow, for example by updating records or executing a code activity.</param>
        public void RegisterWorkflow(Guid workflowId, Action<IOrganizationService, ExecuteWorkflowRequest> run)
        {
            _workflowHandlers[workflowId] = run ?? throw new ArgumentNullException(nameof(run));
        }

        internal Action<IOrganizationService, ExecuteWorkflowRequest> GetWorkflowHandler(Guid workflowId)
        {
            _workflowHandlers.TryGetValue(workflowId, out var run);
            return run;
        }

        internal void RecordExecutedWorkflow(ExecuteWorkflowRequest request)
        {
            _executedWorkflowRequests.Add(request);
        }
    }
}
