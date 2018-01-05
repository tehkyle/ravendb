using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Bundles.Replication.Tasks;
using Raven.Database.Actions;
using Raven.Database.Data;
using Raven.Database.Extensions;
using Raven.Database.Impl;
using Raven.Database.Indexing;
using Raven.Database.Server.WebApi.Attributes;
using Raven.Json.Linq;

namespace Raven.Database.Server.Controllers
{
    public class DocumentsBatchController : ClusterAwareRavenDbApiController
    {
        [HttpPost]
        [RavenRoute("bulk_docs")]
        [RavenRoute("databases/{databaseName}/bulk_docs")]
        public async Task<HttpResponseMessage> BulkPost()
        {
            using (var cts = new CancellationTokenSource())
            using (cts.TimeoutAfter(DatabasesLandlord.SystemConfiguration.DatabaseOperationTimeout))
            {
                RavenJArray jsonCommandArray;

                try
                {
                    jsonCommandArray = await ReadJsonArrayAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException e)
                {
                    if (Log.IsDebugEnabled)
                        Log.DebugException("Failed to read json documents batch.", e);
                    return GetMessageWithObject(new
                    {
                        Message = "Could not understand json, please check its validity."
                    }, (HttpStatusCode)422); //http code 422 - Unprocessable entity

                }
                catch (InvalidDataException e)
                {
                    if (Log.IsDebugEnabled)
                        Log.DebugException("Failed to read json documents batch.", e);
                    return GetMessageWithObject(new
                    {
                        e.Message
                    }, (HttpStatusCode)422); //http code 422 - Unprocessable entity
                }

                cts.Token.ThrowIfCancellationRequested();

                var transactionInformation = GetRequestTransaction();
                var commands =
                    (from RavenJObject jsonCommand in jsonCommandArray
                     select CommandDataFactory.CreateCommand(jsonCommand, transactionInformation))
                     .ToArray();

                if (Log.IsDebugEnabled)
                {
                    Log.Debug(
                        () =>
                        {
                            if (commands.Length > 15) // this is probably an import method, we will input minimal information, to avoid filling up the log
                            {
                                return "\tExecuted "
                                       + string.Join(
                                           ", ", commands.GroupBy(x => x.Method).Select(x => string.Format("{0:#,#;;0} {1} operations", x.Count(), x.Key)));
                            }

                            var sb = new StringBuilder();
                            foreach (var commandData in commands)
                            {
                                sb.AppendFormat("\t{0} {1}{2}", commandData.Method, commandData.Key, Environment.NewLine);
                            }
                            return sb.ToString();
                        });
                }

                //take "snapshots" of NextIndexingRound TaskCompletionSource's --> prevent a race condition
                //between NextIndexingRound completion after the Batch() execution and waiting for it's completion in WaitForIndexesAsync()
                var nextIndexingRoundsByIndexId = new Dictionary<int, Task>();
                var existingIndexes = Database.IndexStorage.GetAllIndexes().ToArray();
                foreach (var index in existingIndexes)
                    nextIndexingRoundsByIndexId.Add(index.indexId,index.NextIndexingRound);

                var batchResult = Database.Batch(commands, cts.Token);

                var writeAssurance = GetHeader("Raven-Write-Assurance");
                if (writeAssurance != null)
                {
                    await WaitForReplicationAsync(writeAssurance, batchResult.LastOrDefault(x => x.Etag != null)).ConfigureAwait(false);
                }

                var waitIndexes = GetHeader("Raven-Wait-Indexes");
                if (waitIndexes != null)
                {
                    //take care to pass existing indexes, in case any index is added or removed between Database.Batch() and WaitForIndexesAsync()
                    //(essentially create "snapshot" of indexes just before the Batch() execution)
                    await WaitForIndexesAsync(waitIndexes, nextIndexingRoundsByIndexId, existingIndexes, batchResult).ConfigureAwait(false);
                }

                return GetMessageWithObject(batchResult);
            }
        }

        private async Task WaitForIndexesAsync(string waitIndexes, Dictionary<int,Task> nextIndexingRoundsByIndexId,Index[] existingIndexes, BatchResult[] results)
        {
            var parts = waitIndexes.Split(new [] {';'},StringSplitOptions.RemoveEmptyEntries);
            var throwOnTimeout = bool.Parse(parts[0]);
            var timeout = TimeSpan.Parse(parts[1]);
            var specificIndexes = new HashSet<string>(parts.Skip(2));

            Etag lastEtag = null;
            var allIndexes = false;
            var modifiedCollections = new HashSet<string>();
            var deletedIds = new HashSet<string>();
            foreach (var batchResult in results)
            {
                if (batchResult.Method == "DELETE")
                    deletedIds.Add(batchResult.Key);

                if (batchResult.Etag == null || batchResult.Metadata == null)
                    continue;

                lastEtag = batchResult.Etag;
                var collection = batchResult.Metadata.Value<string>(Constants.RavenEntityName);
                if (string.IsNullOrEmpty(collection))
                {
                    allIndexes = true;
                    continue;
                }
                modifiedCollections.Add(collection);
            }

            if (lastEtag == null)
            {
                if (deletedIds.Count > 0)
                {
                    while (Database.WorkContext.IndexRemovalQueueContainsAnyFrom(deletedIds))
                        await Task.Delay(100).ConfigureAwait(false);
                }
                return;
            }

            var indexes = new List<Index>();
            foreach (var index in existingIndexes)
            {
                if (specificIndexes.Count > 0)
                {
                    if (specificIndexes.Contains(index.PublicName) == false)
                    {
                        nextIndexingRoundsByIndexId.Remove(index.indexId);
                        continue;
                    }
                }

                if (allIndexes && index.ViewGenerator.ForEntityNames.Count == 0
                    || index.ViewGenerator.ForEntityNames.Overlaps(modifiedCollections))
                {
                    indexes.Add(index);
                }
                else
                {
                    nextIndexingRoundsByIndexId.Remove(index.indexId);
                }
            }

            var sp = Stopwatch.StartNew();
            var needToWait = true;
            var tasks = new Task[indexes.Count + 1];
            var indexingRounds = nextIndexingRoundsByIndexId.Values.ToList();
            do
            {
                needToWait = false;
                Database.TransactionalStorage.Batch(actions =>
                {
                    foreach (var index in indexes)
                    {
                        if (actions.Staleness.IsIndexStale(index.IndexId, null, lastEtag))
                        {
                            needToWait = true;
                            break;
                        }
                    }
                });

                if (needToWait)
                {

                    for (int i = 0; i < indexes.Count; i++)
                    {
                        tasks[i] = indexingRounds[i];
                    }
                    var timeSpan = timeout - sp.Elapsed;
                    if (timeout < TimeSpan.Zero || timeSpan <= TimeSpan.Zero)
                    {
                        if (throwOnTimeout)
                        {
                            throw new TimeoutException("After waiting for " + sp.Elapsed + ", could not verify that " +
                                                       indexes.Count + " indexes has caught up with the chanages as of etag: "
                                                       + lastEtag);
                        }
                        break;
                    }
                    tasks[indexes.Count] = Task.Delay(timeSpan);

                    var result = await Task.WhenAny(tasks).ConfigureAwait(false);

                    if (result == tasks[indexes.Count])
                    {
                        if (throwOnTimeout)
                        {
                            throw new TimeoutException("After waiting for " + sp.Elapsed + ", could not verify that " +
                                                       indexes.Count + " indexes has caught up with the chanages as of etag: "
                                                       + lastEtag);
                        }
                        break;
                    }
                }
            } while (needToWait);
        }

        private async Task WaitForReplicationAsync(string writeAssurance, BatchResult lastResultWithEtag)
        {
            var parts = writeAssurance.Split(';');
            var replicas = int.Parse(parts[0]);
            var timeout = TimeSpan.Parse(parts[1]);
            var throwOnTimeout = bool.Parse(parts[2]);
            var majority = parts[3] == "majority";
            var replicationTask = Database.StartupTasks.OfType<ReplicationTask>().FirstOrDefault();
            if (replicationTask == null)
            {
                if (Log.IsDebugEnabled)
                {
                    Log.Debug("Was asked to get write assurance on a database without replication, ignoring the request");
                }
                return;
            }
            //what can we do if we don't have this?
            if (lastResultWithEtag == null)
                return;

            int numberOfReplicasToWaitFor = majority ? replicationTask.GetSizeOfMajorityFromActiveReplicationDestination(replicas) : replicas;

            var numberOfReplicatesPast = await replicationTask.WaitForReplicationAsync(lastResultWithEtag.Etag, timeout, numberOfReplicasToWaitFor).ConfigureAwait(false);

            if (numberOfReplicatesPast < numberOfReplicasToWaitFor && throwOnTimeout)
            {
                throw new TimeoutException(
                    $"Could not verify that etag {lastResultWithEtag.Etag} was replicated to {numberOfReplicasToWaitFor} servers in {timeout}. So far, it only replicated to {numberOfReplicatesPast}");
            }
            //If we got here than we are either ignoring timeouts or we finished replicating to the required amount of servers.
        }

        [HttpDelete]
        [RavenRoute("bulk_docs/{*id}")]
        [RavenRoute("databases/{databaseName}/bulk_docs/{*id}")]
        public HttpResponseMessage BulkDelete(string id)
        {
            var indexDefinition = Database.IndexDefinitionStorage.GetIndexDefinition(id);
            if (indexDefinition == null)
                throw new IndexDoesNotExistsException(string.Format("Index '{0}' does not exist.", id));

            if (indexDefinition.IsMapReduce)
                throw new InvalidOperationException("Cannot execute DeleteByIndex operation on Map-Reduce indexes.");

            // we don't use using because execution is async
            var cts = new CancellationTokenSource();
            var timeout = cts.TimeoutAfter(DatabasesLandlord.SystemConfiguration.DatabaseOperationTimeout);

            var databaseBulkOperations = new DatabaseBulkOperations(Database, GetRequestTransaction(), cts, timeout);
            return OnBulkOperation((index, query, options, reportProgress) => databaseBulkOperations.DeleteByIndex(index, query, options, reportProgress), id, timeout);
        }

        [HttpPatch]
        [RavenRoute("bulk_docs/{*id}")]
        [RavenRoute("databases/{databaseName}/bulk_docs/{*id}")]
        public async Task<HttpResponseMessage> BulkPatch(string id)
        {
            RavenJArray patchRequestJson;
            try
            {
                patchRequestJson = await ReadJsonArrayAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                if (Log.IsDebugEnabled)
                    Log.DebugException("Failed to read json documents batch.", e);
                return GetMessageWithObject(new
                {
                    Message = "Could not understand json, please check its validity."
                }, (HttpStatusCode)422); //http code 422 - Unprocessable entity

            }
            catch (InvalidDataException e)
            {
                if (Log.IsDebugEnabled)
                    Log.DebugException("Failed to read json documents batch.", e);
                return GetMessageWithObject(new
                {
                    e.Message
                }, (HttpStatusCode)422); //http code 422 - Unprocessable entity
            }

            // we don't use using because execution is async
            var cts = new CancellationTokenSource();
            var timeout = cts.TimeoutAfter(DatabasesLandlord.SystemConfiguration.DatabaseOperationTimeout);

            var databaseBulkOperations = new DatabaseBulkOperations(Database, GetRequestTransaction(), cts, timeout);

            var patchRequests = patchRequestJson.Cast<RavenJObject>().Select(PatchRequest.FromJson).ToArray();
            return OnBulkOperation((index, query, options, reportProgress) => databaseBulkOperations.UpdateByIndex(index, query, patchRequests, options, reportProgress), id, timeout);
        }

        [HttpEval]
        [RavenRoute("bulk_docs/{*id}")]
        [RavenRoute("databases/{databaseName}/bulk_docs/{*id}")]
        public async Task<HttpResponseMessage> BulkEval(string id)
        {
            RavenJObject advPatchRequestJson;

            try
            {
                advPatchRequestJson = await ReadJsonObjectAsync<RavenJObject>().ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                if (Log.IsDebugEnabled)
                    Log.DebugException("Failed to deserialize document batch request.", e);
                return GetMessageWithObject(new
                {
                    Message = "Could not understand json, please check its validity."
                }, (HttpStatusCode)422); //http code 422 - Unprocessable entity

            }
            catch (InvalidDataException e)
            {
                if (Log.IsDebugEnabled)
                    Log.DebugException("Failed to deserialize document batch request.", e);
                return GetMessageWithObject(new
                {
                    e.Message
                }, (HttpStatusCode)422); //http code 422 - Unprocessable entity
            }

            // we don't use using because execution is async
            var cts = new CancellationTokenSource();
            var timeout = cts.TimeoutAfter(DatabasesLandlord.SystemConfiguration.DatabaseOperationTimeout);

            var databaseBulkOperations = new DatabaseBulkOperations(Database, GetRequestTransaction(), cts, timeout);

            var advPatch = ScriptedPatchRequest.FromJson(advPatchRequestJson);
            return OnBulkOperation((index, query, options, reportProgress) => databaseBulkOperations.UpdateByIndex(index, query, advPatch, options, reportProgress), id, timeout);
        }

        private HttpResponseMessage OnBulkOperation(Func<string, IndexQuery, BulkOperationOptions, Action<BulkOperationProgress>, RavenJArray> batchOperation, string index, CancellationTimeout timeout)
        {
            if (string.IsNullOrEmpty(index))
                return GetEmptyMessage(HttpStatusCode.BadRequest);

            var option = new BulkOperationOptions
            {
                AllowStale = GetAllowStale(),
                MaxOpsPerSec = GetMaxOpsPerSec(),
                StaleTimeout = GetStaleTimeout(),
                RetrieveDetails = GetRetrieveDetails()
            };

            var indexQuery = GetIndexQuery(maxPageSize: int.MaxValue);

            var status = new BulkOperationStatus();
            long id;

            var task = Task.Factory.StartNew(() =>
            {
                using (DocumentCacher.SkipSetDocumentsInDocumentCache())
                {
                    var batchResult = batchOperation(index, indexQuery, option, x =>
                    {
                        status.MarkProgress(x);
                    });

                    lock (status.State)
                    {
                        status.State["Batch"] = batchResult;
                    }
                }

            }).ContinueWith(t =>
            {
                if (timeout != null)
                    timeout.Dispose();

                if (t.IsFaulted == false)
                {
                    status.MarkCompleted($"Processed {status.OperationProgress.ProcessedEntries} items");
                    return;
                }

                var exception = t.Exception.ExtractSingleInnerException();

                status.MarkFaulted(exception.Message);
            });

            Database.Tasks.AddTask(task, status, new TaskActions.PendingTaskDescription
            {
                StartTime = SystemTime.UtcNow,
                TaskType = TaskActions.PendingTaskType.IndexBulkOperation,
                Description = index
            }, out id, timeout.CancellationTokenSource);

            return GetMessageWithObject(new { OperationId = id }, HttpStatusCode.Accepted);
        }

        public class BulkOperationStatus : OperationStateBase
        {
            public RavenJArray Result { get; set; }
            public BulkOperationProgress OperationProgress { get; private set; }

            public BulkOperationStatus()
            {
                OperationProgress = new BulkOperationProgress();
            }

            public void MarkProgress(BulkOperationProgress progress)
            {
                OperationProgress.ProcessedEntries = progress.ProcessedEntries;
                OperationProgress.TotalEntries = progress.TotalEntries;
                MarkProgress($"Processed {progress.ProcessedEntries}/{progress.TotalEntries} items");
            }
        }
    }
}
