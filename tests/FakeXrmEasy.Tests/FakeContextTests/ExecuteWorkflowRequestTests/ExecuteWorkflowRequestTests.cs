using System;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FakeXrmEasy.Tests.FakeContextTests.ExecuteWorkflowRequestTests
{
    public class ExecuteWorkflowRequestTests
    {
        private readonly XrmFakedContext _context = new XrmFakedContext();
        private readonly IOrganizationService _service;
        private readonly Guid _workflowId = Guid.NewGuid();
        private readonly Guid _accountId = Guid.NewGuid();

        public ExecuteWorkflowRequestTests()
        {
            _service = _context.GetOrganizationService();
            _context.Initialize(new Entity("account") { Id = _accountId });
        }

        [Fact]
        public void Unregistered_workflow_is_recorded_and_left_waiting()
        {
            var response = Execute();

            var request = Assert.Single(_context.ExecutedWorkflowRequests);
            Assert.Equal(_workflowId, request.WorkflowId);
            Assert.Equal(_accountId, request.EntityId);

            var job = _service.Retrieve("asyncoperation", response.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(0, job.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(_workflowId, job.GetAttributeValue<EntityReference>("workflowactivationid").Id);
        }

        [Fact]
        public void Registered_workflow_runs_and_completes()
        {
            _context.RegisterWorkflow(_workflowId, (service, request) =>
                service.Update(new Entity("account") { Id = request.EntityId, ["name"] = "Processed" }));

            var response = Execute();

            Assert.Equal("Processed", _service.Retrieve("account", _accountId, new Microsoft.Xrm.Sdk.Query.ColumnSet("name")).GetAttributeValue<string>("name"));
            var job = _service.Retrieve("asyncoperation", response.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(3, job.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(30, job.GetAttributeValue<OptionSetValue>("statuscode").Value);
        }

        [Fact]
        public void Failing_workflow_marks_the_job_failed_without_throwing()
        {
            _context.RegisterWorkflow(_workflowId, (service, request) => throw new InvalidPluginExecutionException("boom"));

            var response = Execute();

            var job = _service.Retrieve("asyncoperation", response.Id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(31, job.GetAttributeValue<OptionSetValue>("statuscode").Value);
            Assert.Equal("boom", job.GetAttributeValue<string>("message"));
        }

        [Fact]
        public void Missing_workflow_id_throws()
        {
            Assert.Throws<FaultException<OrganizationServiceFault>>(() =>
                _service.Execute(new ExecuteWorkflowRequest { EntityId = _accountId }));
        }

        private ExecuteWorkflowResponse Execute()
        {
            return (ExecuteWorkflowResponse)_service.Execute(new ExecuteWorkflowRequest { WorkflowId = _workflowId, EntityId = _accountId });
        }
    }
}
