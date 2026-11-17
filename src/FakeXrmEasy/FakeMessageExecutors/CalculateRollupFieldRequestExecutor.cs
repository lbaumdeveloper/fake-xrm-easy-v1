using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.FakeMessageExecutors
{
    /// <summary>
    /// Calculates a rollup field with the calculation registered through <see cref="XrmFakedContext.RegisterRollupField"/>
    /// and stores the value, its calculation date and state on the target record without running pipeline steps.
    /// </summary>
    public class CalculateRollupFieldRequestExecutor : IFakeMessageExecutor
    {
        private const int CalculatedRollupState = 1;

        public bool CanExecute(OrganizationRequest request)
        {
            return request is CalculateRollupFieldRequest;
        }

        public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
        {
            var req = (CalculateRollupFieldRequest)request;

            if (req.Target == null)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "Target is required.");
            }

            if (string.IsNullOrWhiteSpace(req.FieldName))
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "FieldName is required.");
            }

            var calculate = ctx.GetRollupCalculator(req.Target.LogicalName, req.FieldName);
            if (calculate == null)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument,
                    $"No rollup calculation is registered for {req.Target.LogicalName}.{req.FieldName}. Call XrmFakedContext.RegisterRollupField first.");
            }

            if (!ctx.Data.TryGetValue(req.Target.LogicalName, out var records) || !records.TryGetValue(req.Target.Id, out var record))
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.ObjectDoesNotExist, $"{req.Target.LogicalName} with Id {req.Target.Id} does not exist.");
                return null;
            }

            record[req.FieldName] = calculate(ctx.GetOrganizationService(), req.Target);
            record[req.FieldName + "_date"] = DateTime.UtcNow;
            record[req.FieldName + "_state"] = CalculatedRollupState;

            var result = new Entity(req.Target.LogicalName) { Id = req.Target.Id };
            foreach (var attribute in new[] { req.FieldName, req.FieldName + "_date", req.FieldName + "_state" })
            {
                result[attribute] = record[attribute];
            }

            return new CalculateRollupFieldResponse
            {
                Results = new ParameterCollection { { "Entity", result } }
            };
        }

        public Type GetResponsibleRequestType()
        {
            return typeof(CalculateRollupFieldRequest);
        }
    }
}
