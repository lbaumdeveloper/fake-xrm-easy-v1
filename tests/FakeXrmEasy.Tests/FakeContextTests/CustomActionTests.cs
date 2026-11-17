using System;
using System.Runtime.Serialization;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace FakeXrmEasy.Tests.FakeContextTests
{
    public class CustomActionTests
    {
        private readonly XrmFakedContext _context = new XrmFakedContext();

        [Fact]
        public void Registered_action_maps_inputs_to_outputs()
        {
            _context.RegisterCustomAction("new_Calculate", inputs => new ParameterCollection
            {
                { "Result", (int)inputs["A"] + (int)inputs["B"] }
            });

            var response = _context.GetOrganizationService().Execute(new OrganizationRequest("new_Calculate")
            {
                ["A"] = 2,
                ["B"] = 3
            });

            Assert.Equal(5, response["Result"]);
            Assert.Equal("new_Calculate", response.ResponseName);
        }

        [Fact]
        public void Registered_action_can_use_the_organization_service()
        {
            var accountId = Guid.NewGuid();
            _context.Initialize(new Entity("account") { Id = accountId, ["name"] = "Contoso" });
            _context.RegisterCustomAction("new_GetName", (service, request) => new ParameterCollection
            {
                { "Name", service.Retrieve("account", ((EntityReference)request["Target"]).Id, new Microsoft.Xrm.Sdk.Query.ColumnSet("name")).GetAttributeValue<string>("name") }
            });

            var response = _context.GetOrganizationService().Execute(new OrganizationRequest("new_GetName")
            {
                ["Target"] = new EntityReference("account", accountId)
            });

            Assert.Equal("Contoso", response["Name"]);
        }

        [Fact]
        public void Registered_action_handles_early_bound_request_classes()
        {
            _context.RegisterCustomAction("new_Ping", inputs => new ParameterCollection { { "Pong", true } });

            var response = _context.GetOrganizationService().Execute(new new_PingRequest());

            Assert.Equal(true, response["Pong"]);
        }

        [DataContract(Namespace = "http://schemas.microsoft.com/xrm/2011/new/")]
        private class new_PingRequest : OrganizationRequest
        {
            public new_PingRequest()
            {
                RequestName = "new_Ping";
            }
        }
    }
}
