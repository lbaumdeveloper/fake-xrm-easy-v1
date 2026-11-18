using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace FakeXrmEasy.FakeMessageExecutors
{
    /// <summary>
    /// Merges a subordinate account, contact, lead or incident into the master record: copies UpdateContent to the master,
    /// points every lookup that referenced the subordinate to the master, and deactivates the subordinate with masterid and merged set.
    /// </summary>
    public class MergeRequestExecutor : IFakeMessageExecutor
    {
        private static readonly Dictionary<string, (int State, int Status)> InactiveStates = new Dictionary<string, (int, int)>
        {
            { "account", (1, 2) },
            { "contact", (1, 2) },
            { "lead", (2, 7) },
            { "incident", (2, 2000) }
        };

        public bool CanExecute(OrganizationRequest request)
        {
            return request is MergeRequest;
        }

        public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
        {
            var req = (MergeRequest)request;

            if (req.Target == null)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "Target is required.");
            }

            if (req.SubordinateId == Guid.Empty)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "SubordinateId is required.");
            }

            if (!InactiveStates.TryGetValue(req.Target.LogicalName, out var inactive))
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, $"Merge is not supported for entity {req.Target.LogicalName}.");
            }

            if (req.Target.Id == req.SubordinateId)
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.InvalidArgument, "The master and subordinate records must be different.");
            }

            var master = GetRecord(ctx, req.Target.LogicalName, req.Target.Id);
            var subordinate = GetRecord(ctx, req.Target.LogicalName, req.SubordinateId);

            if (req.UpdateContent != null && req.UpdateContent.Attributes.Count > 0)
            {
                var update = new Entity(master.LogicalName) { Id = master.Id };
                foreach (var attribute in req.UpdateContent.Attributes)
                {
                    update[attribute.Key] = attribute.Value;
                }
                ctx.GetOrganizationService().Update(update);
            }

            ReparentReferences(ctx, subordinate, req.Target);

            subordinate["statecode"] = new OptionSetValue(inactive.State);
            subordinate["statuscode"] = new OptionSetValue(inactive.Status);
            subordinate["merged"] = true;
            subordinate["masterid"] = new EntityReference(req.Target.LogicalName, req.Target.Id);

            return new MergeResponse();
        }

        public Type GetResponsibleRequestType()
        {
            return typeof(MergeRequest);
        }

        private static Entity GetRecord(XrmFakedContext ctx, string logicalName, Guid id)
        {
            if (!ctx.Data.TryGetValue(logicalName, out var records) || !records.TryGetValue(id, out var record))
            {
                FakeOrganizationServiceFault.Throw(ErrorCodes.ObjectDoesNotExist, $"{logicalName} with Id {id} does not exist.");
                return null;
            }

            return record;
        }

        private static void ReparentReferences(XrmFakedContext ctx, Entity subordinate, EntityReference master)
        {
            var records = ctx.Data.Values.SelectMany(r => r.Values).Where(r => !ReferenceEquals(r, subordinate));
            foreach (var record in records)
            {
                var referencing = record.Attributes
                    .Where(a => a.Value is EntityReference reference
                             && reference.Id == subordinate.Id
                             && reference.LogicalName == subordinate.LogicalName)
                    .Select(a => a.Key)
                    .ToList();

                foreach (var attribute in referencing)
                {
                    record[attribute] = new EntityReference(master.LogicalName, master.Id);
                }
            }
        }
    }
}
