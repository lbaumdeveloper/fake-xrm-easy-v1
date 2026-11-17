using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FakeXrmEasy
{
    /// <summary>
    /// Standard rollup aggregates over related records, for use with <see cref="XrmFakedContext.RegisterRollupField"/>.
    /// </summary>
    public static class RollupFields
    {
        public static Func<IOrganizationService, EntityReference, object> Count(string relatedEntity, string lookupAttribute, FilterExpression filter = null)
        {
            return (service, target) => RetrieveRelated(service, target, relatedEntity, lookupAttribute, null, filter).Count;
        }

        public static Func<IOrganizationService, EntityReference, object> Sum(string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter = null)
        {
            return (service, target) => Aggregate(RelatedValues(service, target, relatedEntity, lookupAttribute, valueAttribute, filter), values => values.Sum(), 0m);
        }

        public static Func<IOrganizationService, EntityReference, object> Avg(string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter = null)
        {
            return (service, target) => Aggregate(RelatedValues(service, target, relatedEntity, lookupAttribute, valueAttribute, filter), values => values.Average(), null);
        }

        public static Func<IOrganizationService, EntityReference, object> Min(string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter = null)
        {
            return (service, target) => Extreme(RelatedValues(service, target, relatedEntity, lookupAttribute, valueAttribute, filter), descending: false);
        }

        public static Func<IOrganizationService, EntityReference, object> Max(string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter = null)
        {
            return (service, target) => Extreme(RelatedValues(service, target, relatedEntity, lookupAttribute, valueAttribute, filter), descending: true);
        }

        private static List<Entity> RetrieveRelated(IOrganizationService service, EntityReference target, string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter)
        {
            var query = new QueryExpression(relatedEntity)
            {
                ColumnSet = valueAttribute != null ? new ColumnSet(valueAttribute) : new ColumnSet(false)
            };
            query.Criteria.AddCondition(lookupAttribute, ConditionOperator.Equal, target.Id);
            if (filter != null)
            {
                query.Criteria.AddFilter(filter);
            }

            return service.RetrieveMultiple(query).Entities.ToList();
        }

        private static List<object> RelatedValues(IOrganizationService service, EntityReference target, string relatedEntity, string lookupAttribute, string valueAttribute, FilterExpression filter)
        {
            return RetrieveRelated(service, target, relatedEntity, lookupAttribute, valueAttribute, filter)
                .Where(e => e.Contains(valueAttribute) && e[valueAttribute] != null)
                .Select(e => e[valueAttribute])
                .ToList();
        }

        private static object Aggregate(List<object> values, Func<IEnumerable<decimal>, decimal> aggregate, decimal? emptyResult)
        {
            if (values.Count == 0)
            {
                return emptyResult;
            }

            var result = aggregate(values.Select(ToDecimal));
            return values[0] is Money ? new Money(result) : (object)result;
        }

        private static object Extreme(List<object> values, bool descending)
        {
            if (values.Count == 0)
            {
                return null;
            }

            if (values[0] is DateTime)
            {
                var dates = values.Cast<DateTime>();
                return descending ? dates.Max() : dates.Min();
            }

            var ordered = descending ? values.OrderByDescending(ToDecimal) : values.OrderBy(ToDecimal);
            return ordered.First();
        }

        private static decimal ToDecimal(object value)
        {
            return value is Money money ? money.Value : Convert.ToDecimal(value);
        }
    }
}
