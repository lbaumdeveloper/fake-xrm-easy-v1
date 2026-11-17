using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy
{
    public partial class XrmFakedContext
    {
        private readonly Dictionary<string, Func<IOrganizationService, EntityReference, object>> _rollupCalculators =
            new Dictionary<string, Func<IOrganizationService, EntityReference, object>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers how a rollup field is calculated when a CalculateRollupFieldRequest is executed.
        /// Use the helpers in <see cref="RollupFields"/> for the standard aggregates or pass a custom calculation.
        /// </summary>
        /// <param name="entityLogicalName">The entity that owns the rollup field.</param>
        /// <param name="fieldName">The rollup field.</param>
        /// <param name="calculate">Returns the field value for the given record.</param>
        public void RegisterRollupField(string entityLogicalName, string fieldName, Func<IOrganizationService, EntityReference, object> calculate)
        {
            _rollupCalculators[RollupKey(entityLogicalName, fieldName)] = calculate ?? throw new ArgumentNullException(nameof(calculate));
        }

        internal Func<IOrganizationService, EntityReference, object> GetRollupCalculator(string entityLogicalName, string fieldName)
        {
            _rollupCalculators.TryGetValue(RollupKey(entityLogicalName, fieldName), out var calculate);
            return calculate;
        }

        private static string RollupKey(string entityLogicalName, string fieldName)
        {
            return entityLogicalName + "." + fieldName;
        }
    }
}
