//-----------------------------------------------------------------------
// <copyright file="ExpirationReadTrigger.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.ComponentModel.Composition;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Database.Plugins;
using Raven.Json.Linq;

namespace Raven.Bundles.Expiration
{
    [InheritedExport(typeof(AbstractReadTrigger))]
    [ExportMetadata("Bundle", "DocumentExpiration")]
    public class ExpirationReadTrigger : AbstractReadTrigger
    {
        public const string RavenExpirationDate = "Raven-Expiration-Date";

        public override ReadVetoResult AllowRead(string key, RavenJObject metadata, ReadOperation operation,
                                                 TransactionInformation transactionInformation)
        {
            if (operation == ReadOperation.Index)
                return ReadVetoResult.Allowed; // we have to allow indexing, because we are deleting using the index
            if(metadata == null)
                return ReadVetoResult.Allowed;
            var property = metadata[RavenExpirationDate];
            if (property == null)
                return ReadVetoResult.Allowed;
            DateTime dateTime;
            try
            {
                dateTime = property.Value<DateTime>();
            }
            catch (Exception)
            {
                // if we can't process the value, ignore it.
                return ReadVetoResult.Allowed;
            }
            if(dateTime > SystemTime.UtcNow)
                return ReadVetoResult.Allowed;
            return ReadVetoResult.Ignore;
        }

    }
}
