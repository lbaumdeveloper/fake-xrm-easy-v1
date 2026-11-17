using System;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace FakeXrmEasy.Tests.FakeContextTests
{
    public class MergeRequestTests
    {
        private readonly XrmFakedContext _context = new XrmFakedContext();
        private readonly IOrganizationService _service;
        private readonly Guid _masterId = Guid.NewGuid();
        private readonly Guid _subordinateId = Guid.NewGuid();
        private readonly Guid _contactId = Guid.NewGuid();

        public MergeRequestTests()
        {
            _service = _context.GetOrganizationService();
            _context.Initialize(new[]
            {
                new Entity("account") { Id = _masterId, ["name"] = "Master", ["statecode"] = new OptionSetValue(0) },
                new Entity("account") { Id = _subordinateId, ["name"] = "Duplicate", ["telephone1"] = "123", ["statecode"] = new OptionSetValue(0) },
                new Entity("contact") { Id = _contactId, ["parentcustomerid"] = new EntityReference("account", _subordinateId) }
            });
        }

        [Fact]
        public void Merge_deactivates_subordinate_and_links_it_to_master()
        {
            Merge(new Entity("account"));

            var subordinate = Retrieve("account", _subordinateId);
            Assert.Equal(1, subordinate.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(2, subordinate.GetAttributeValue<OptionSetValue>("statuscode").Value);
            Assert.True(subordinate.GetAttributeValue<bool>("merged"));
            Assert.Equal(_masterId, subordinate.GetAttributeValue<EntityReference>("masterid").Id);
        }

        [Fact]
        public void Merge_copies_update_content_to_master()
        {
            Merge(new Entity("account") { ["telephone1"] = "123" });

            var master = Retrieve("account", _masterId);
            Assert.Equal("123", master.GetAttributeValue<string>("telephone1"));
            Assert.Equal("Master", master.GetAttributeValue<string>("name"));
        }

        [Fact]
        public void Merge_moves_child_records_to_master()
        {
            Merge(new Entity("account"));

            Assert.Equal(_masterId, Retrieve("contact", _contactId).GetAttributeValue<EntityReference>("parentcustomerid").Id);
        }

        [Fact]
        public void Merge_of_unsupported_entity_throws()
        {
            Assert.Throws<FaultException<OrganizationServiceFault>>(() => _service.Execute(new MergeRequest
            {
                Target = new EntityReference("opportunity", Guid.NewGuid()),
                SubordinateId = Guid.NewGuid(),
                UpdateContent = new Entity("opportunity")
            }));
        }

        [Fact]
        public void Merge_with_missing_subordinate_throws()
        {
            Assert.Throws<FaultException<OrganizationServiceFault>>(() => _service.Execute(new MergeRequest
            {
                Target = new EntityReference("account", _masterId),
                SubordinateId = Guid.NewGuid(),
                UpdateContent = new Entity("account")
            }));
        }

        private void Merge(Entity updateContent)
        {
            _service.Execute(new MergeRequest
            {
                Target = new EntityReference("account", _masterId),
                SubordinateId = _subordinateId,
                UpdateContent = updateContent
            });
        }

        private Entity Retrieve(string entityName, Guid id)
        {
            return _service.Retrieve(entityName, id, new ColumnSet(true));
        }
    }
}
