using Raven.Abstractions.Data;
using Raven.Abstractions.Util;
using Raven.Client.Changes;
using Raven.Client.Connection.Async;
using Raven.Client.Util;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Client.Document
{
    public class ChunkedRemoteBulkInsertOperation : ILowLevelBulkInsertOperation
    {
        private readonly BulkInsertOptions options;

        private readonly AsyncServerClient client;

        private readonly IDatabaseChanges changes;
                
        private int processedItemsInCurrentOperation;

        private RemoteBulkInsertOperation current;
                
        private long currentChunkSize;

        private bool disposed;

        private Task<int> previousTask;

        public ChunkedRemoteBulkInsertOperation(BulkInsertOptions options, AsyncServerClient client, IDatabaseChanges changes)
        {
            this.options = options;
            this.client = client;
            this.changes = changes;			
            currentChunkSize = 0;
            current = GetBulkInsertOperation();
        }

        public Guid OperationId
        {
            get
            {
                return current == null ? Guid.Empty : current.OperationId;
            }
        }

        public void Write(string id, RavenJObject metadata, RavenJObject data, int? dataSize)
        {
            current = GetBulkInsertOperation();

            current.Write(id, metadata, data, dataSize);

            if (options.ChunkedBulkInsertOptions.MaxChunkVolumeInBytes > 0)
                currentChunkSize += DocumentHelpers.GetRoughSize(data);

            processedItemsInCurrentOperation++;
        }

        public async Task WaitForLastTaskToFinish()
        {
            if (disposed == false && current != null)
            {
                await current.DisposeAsync().ConfigureAwait(false);
                current = null;
            }
                
        }

        private RemoteBulkInsertOperation GetBulkInsertOperation()
        {
            if (current == null)
                return current = CreateBulkInsertOperation(Task.FromResult(0));

            if (processedItemsInCurrentOperation < options.ChunkedBulkInsertOptions.MaxDocumentsPerChunk)
                if (options.ChunkedBulkInsertOptions.MaxChunkVolumeInBytes <= 0 || currentChunkSize < options.ChunkedBulkInsertOptions.MaxChunkVolumeInBytes)
                    return current;

            // if we haven't flushed the previous one yet, we will force 
            // a disposal of both the previous one and the one before, to avoid 
            // consuming a lot of memory, and to have _too_ much concurrency.
            if (previousTask != null)
            {
                previousTask.ConfigureAwait(false).GetAwaiter().GetResult();
            }
            previousTask = current.DisposeAsync();

            currentChunkSize = 0;
            processedItemsInCurrentOperation = 0;
            current = CreateBulkInsertOperation(previousTask);
            return current;
        }

        private RemoteBulkInsertOperation CreateBulkInsertOperation(Task<int> disposeAsync)
        {
            Guid? existingOperationId;
            if (OperationId == Guid.Empty)
                existingOperationId = null;
            else
            existingOperationId = OperationId;
            var operation = new RemoteBulkInsertOperation(options, client, changes, disposeAsync, existingOperationId);
            if (Report != null)
                operation.Report += Report;

            return operation;
        }

        public async Task<int> DisposeAsync()
        {
            if (disposed)
                return -1;

            disposed = true;

            if (current != null)
                return await current.DisposeAsync().ConfigureAwait(false);

            return 0;
        }

        public event Action<string> Report;
        public void Abort()
        {
            current.Abort();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            using (NoSynchronizationContext.Scope())
            {
                var disposeAsync = DisposeAsync().ConfigureAwait(false);
                disposeAsync.GetAwaiter().GetResult();
            }
        }
        
        public bool IsAborted
        {
            get { return current != null && current.IsAborted; }
        }
    }
}
