using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace FakeXrmEasy.Tests.FakeContextTests.TeamOperators
{
    public class TeamOperatorTests
    {
        private readonly XrmFakedContext _context;
        private readonly IOrganizationService _service;
        private readonly Guid _userAccountId = Guid.NewGuid();
        private readonly Guid _myTeamAccountId = Guid.NewGuid();
        private readonly Guid _otherTeamAccountId = Guid.NewGuid();

        public TeamOperatorTests()
        {
            var callerId = Guid.NewGuid();
            _context = new XrmFakedContext { CallerId = new EntityReference("systemuser", callerId) };
            _service = _context.GetOrganizationService();

            var myTeam = new Entity("team") { Id = Guid.NewGuid() };
            var otherTeam = new Entity("team") { Id = Guid.NewGuid() };

            _context.Initialize(new[]
            {
                myTeam,
                otherTeam,
                new Entity("teammembership") { Id = Guid.NewGuid(), ["teamid"] = myTeam.Id, ["systemuserid"] = callerId },
                new Entity("account") { Id = _userAccountId, ["ownerid"] = new EntityReference("systemuser", callerId) },
                new Entity("account") { Id = _myTeamAccountId, ["ownerid"] = myTeam.ToEntityReference() },
                new Entity("account") { Id = _otherTeamAccountId, ["ownerid"] = otherTeam.ToEntityReference() }
            });
        }

        [Fact]
        public void EqualUserTeams_returns_records_owned_by_the_callers_teams()
        {
            var ids = Query(ConditionOperator.EqualUserTeams);

            Assert.Equal(new[] { _myTeamAccountId }, ids);
        }

        [Fact]
        public void EqualUserOrUserTeams_returns_records_owned_by_the_caller_or_the_callers_teams()
        {
            var ids = Query(ConditionOperator.EqualUserOrUserTeams);

            Assert.Equal(2, ids.Length);
            Assert.Contains(_userAccountId, ids);
            Assert.Contains(_myTeamAccountId, ids);
        }

        [Fact]
        public void EqualUserTeams_returns_nothing_when_the_caller_is_in_no_team()
        {
            _context.CallerId = new EntityReference("systemuser", Guid.NewGuid());

            Assert.Empty(Query(ConditionOperator.EqualUserTeams));
        }

        [Theory]
        [InlineData("eq-userteams", 1)]
        [InlineData("eq-useroruserteams", 2)]
        public void Fetch_team_operators_are_supported(string fetchOperator, int expectedCount)
        {
            var fetchXml = $@"<fetch>
                <entity name='account'>
                    <attribute name='accountid' />
                    <filter><condition attribute='ownerid' operator='{fetchOperator}' /></filter>
                </entity>
            </fetch>";

            var result = _service.RetrieveMultiple(new FetchExpression(fetchXml));

            Assert.Equal(expectedCount, result.Entities.Count);
        }

        private Guid[] Query(ConditionOperator conditionOperator)
        {
            var query = new QueryExpression("account") { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition("ownerid", conditionOperator);
            return _service.RetrieveMultiple(query).Entities.Select(e => e.Id).ToArray();
        }
    }
}
