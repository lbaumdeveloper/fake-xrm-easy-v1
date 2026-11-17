using System;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace FakeXrmEasy.Tests.FakeContextTests.CalculateRollupFieldRequestTests
{
    public class CalculateRollupFieldRequestTests
    {
        private readonly XrmFakedContext _context;
        private readonly IOrganizationService _service;
        private readonly EntityReference _account;

        public CalculateRollupFieldRequestTests()
        {
            _context = new XrmFakedContext();
            _service = _context.GetOrganizationService();
            _account = new EntityReference("account", Guid.NewGuid());

            _context.Initialize(new[]
            {
                new Entity("account") { Id = _account.Id },
                Opportunity(100m, 0),
                Opportunity(250m, 0),
                Opportunity(1000m, 1),
                new Entity("opportunity") { Id = Guid.NewGuid(), ["parentaccountid"] = new EntityReference("account", Guid.NewGuid()), ["estimatedvalue"] = new Money(5m) }
            });
        }

        [Fact]
        public void Sum_rollup_is_calculated_and_stored_on_the_record()
        {
            _context.RegisterRollupField("account", "new_openrevenue",
                RollupFields.Sum("opportunity", "parentaccountid", "estimatedvalue", Open()));

            var response = Calculate("new_openrevenue");

            Assert.Equal(350m, response.Entity.GetAttributeValue<Money>("new_openrevenue").Value);
            Assert.Equal(1, response.Entity.GetAttributeValue<int>("new_openrevenue_state"));
            Assert.NotNull(response.Entity.GetAttributeValue<DateTime?>("new_openrevenue_date"));

            var stored = _service.Retrieve("account", _account.Id, new ColumnSet("new_openrevenue"));
            Assert.Equal(350m, stored.GetAttributeValue<Money>("new_openrevenue").Value);
        }

        [Fact]
        public void Count_rollup_counts_related_records()
        {
            _context.RegisterRollupField("account", "new_opportunitycount", RollupFields.Count("opportunity", "parentaccountid"));

            Assert.Equal(3, Calculate("new_opportunitycount").Entity.GetAttributeValue<int>("new_opportunitycount"));
        }

        [Fact]
        public void Min_max_and_avg_rollups_are_calculated()
        {
            _context.RegisterRollupField("account", "new_min", RollupFields.Min("opportunity", "parentaccountid", "estimatedvalue"));
            _context.RegisterRollupField("account", "new_max", RollupFields.Max("opportunity", "parentaccountid", "estimatedvalue"));
            _context.RegisterRollupField("account", "new_avg", RollupFields.Avg("opportunity", "parentaccountid", "estimatedvalue"));

            Assert.Equal(100m, Calculate("new_min").Entity.GetAttributeValue<Money>("new_min").Value);
            Assert.Equal(1000m, Calculate("new_max").Entity.GetAttributeValue<Money>("new_max").Value);
            Assert.Equal(450m, Calculate("new_avg").Entity.GetAttributeValue<Money>("new_avg").Value);
        }

        [Fact]
        public void Custom_calculation_receives_the_target()
        {
            EntityReference received = null;
            _context.RegisterRollupField("account", "new_custom", (service, target) =>
            {
                received = target;
                return 42;
            });

            Assert.Equal(42, Calculate("new_custom").Entity.GetAttributeValue<int>("new_custom"));
            Assert.Equal(_account.Id, received.Id);
        }

        [Fact]
        public void Unregistered_rollup_field_throws()
        {
            Assert.Throws<FaultException<OrganizationServiceFault>>(() => Calculate("new_unknown"));
        }

        [Fact]
        public void Missing_target_record_throws()
        {
            _context.RegisterRollupField("account", "new_opportunitycount", RollupFields.Count("opportunity", "parentaccountid"));

            Assert.Throws<FaultException<OrganizationServiceFault>>(() => _service.Execute(new CalculateRollupFieldRequest
            {
                Target = new EntityReference("account", Guid.NewGuid()),
                FieldName = "new_opportunitycount"
            }));
        }

        private CalculateRollupFieldResponse Calculate(string fieldName)
        {
            return (CalculateRollupFieldResponse)_service.Execute(new CalculateRollupFieldRequest { Target = _account, FieldName = fieldName });
        }

        private Entity Opportunity(decimal value, int statecode)
        {
            return new Entity("opportunity")
            {
                Id = Guid.NewGuid(),
                ["parentaccountid"] = _account,
                ["estimatedvalue"] = new Money(value),
                ["statecode"] = new OptionSetValue(statecode)
            };
        }

        private static FilterExpression Open()
        {
            var filter = new FilterExpression();
            filter.AddCondition("statecode", ConditionOperator.Equal, 0);
            return filter;
        }
    }
}
