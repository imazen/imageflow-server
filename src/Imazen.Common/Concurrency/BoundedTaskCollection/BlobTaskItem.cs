// Copyright (c) Imazen LLC.
// No part of this project, including this file, may be copied, modified,
// propagated, or distributed except as permitted in COPYRIGHT.txt.
// Licensed under the GNU Affero General Public License, Version 3.0.
// Commercial licenses available at http://imageresizing.net/

using Imazen.Abstractions.Blobs;

namespace Imazen.Common.Concurrency.BoundedTaskCollection {
    public sealed class BlobTaskItem : IBoundedTaskItem, IDisposable {

        /// <summary>
        /// Throws an exception if the blob is not natively reusable (call EnsureReusable first)
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <exception cref="ArgumentException"></exception>
        public BlobTaskItem(string key, IBlobWrapper data) {
            //if (!data.IsReusable) throw new System.ArgumentException("Blob must be natively reusable", nameof(data));
            data.IndicateInterest();
            this.data = data;
            UniqueKey = key;
            JobCreatedAt = DateTime.UtcNow;
            estimateAllocatedBytes = data.EstimateAllocatedBytes ?? 0;
        }
        private readonly IBlobWrapper data;
        
        public IBlobWrapper Blob => data;
        
        private readonly long estimateAllocatedBytes;
        private Task? RunningTask { get; set; }
        public void StoreStartedTask(Task task)
        {
            if (RunningTask != null) throw new InvalidOperationException("Task already stored");
            RunningTask = task;
        }

        public Task? GetTask()
        {
            return RunningTask;
        }

        /// <summary>
        /// Returns the UTC time this AsyncWrite object was created.
        /// </summary>
        public DateTime JobCreatedAt { get; }

        public string UniqueKey { get; }

        /// <summary>
        /// Estimates the allocation size of this object structure
        /// </summary>
        /// <returns></returns>
        public long GetTaskSizeInMemory()
        {
            return estimateAllocatedBytes + 100;
        }

        public void Dispose()
        {
            data?.Dispose();
        }
        
    }
}
