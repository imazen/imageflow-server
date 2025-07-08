// Copyright (c) Imazen LLC.
// No part of this project, including this file, may be copied, modified,
// propagated, or distributed except as permitted in COPYRIGHT.txt.
// Licensed under the GNU Affero General Public License, Version 3.0.
// Commercial licenses available at http://imageresizing.net/

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;

namespace Imazen.Common.Concurrency.BoundedTaskCollection {
    public class BoundedTaskCollection<T>: IHostedService where T: class, IBoundedTaskItem {

        public BoundedTaskCollection(long maxQueueBytes, CancellationTokenSource? cts =null) {
            MaxQueueBytes = maxQueueBytes;
            this.cts = cts ?? new CancellationTokenSource();
        }
        
        private CancellationTokenSource cts;

        private readonly object syncStopping = new object();
        
        private volatile bool stopped = false;

        private readonly ConcurrentDictionary<string, T> c = new ConcurrentDictionary<string, T>();

        /// <summary>
        /// How many bytes of data to hold in memory before refusing further queue requests and forcing them to be executed synchronously.
        /// </summary>
        public long MaxQueueBytes { get; }

        private long queuedBytes;
        /// <summary>
        /// If the collection contains the specified item, it is returned. Otherwise, null is returned.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public T? Get(string key) {
            c.TryGetValue(key, out var result);
            return result;
        }
        public bool TryGet(string key, [NotNullWhen(true)]out T? result) {
            return c.TryGetValue(key, out result);
        }

        /// <summary>
        /// Tries to enqueue the given task and callback
        /// </summary>
        /// <param name="taskItem"></param>
        /// <param name="taskProcessor"></param>
        /// <returns></returns>
        public TaskEnqueueResult Queue(T taskItem, Func<T, CancellationToken, Task> taskProcessor){
            if (stopped) return TaskEnqueueResult.Stopped;
            if (queuedBytes < 0) throw new InvalidOperationException();
            
            // Deal with maximum queue size
            var taskSize = taskItem.GetTaskSizeInMemory();
            Interlocked.Add(ref queuedBytes, taskSize);
            if (queuedBytes > MaxQueueBytes) {
                Interlocked.Add(ref queuedBytes, -taskSize);
                return TaskEnqueueResult.QueueFull; //Because we would use too much ram.
            }
            if (stopped) return TaskEnqueueResult.Stopped;
            lock (syncStopping)
            {
                if (stopped) return TaskEnqueueResult.Stopped;
                // Deal with duplicates
                if (!c.TryAdd(taskItem.UniqueKey, taskItem))
                {
                    Interlocked.Add(ref queuedBytes, -taskSize);
                    return TaskEnqueueResult.AlreadyPresent;
                }


                // Allocate the task
                taskItem.StoreStartedTask(Task.Run(
                    async () =>
                    {
                        try
                        {
                            if (cts.IsCancellationRequested) return;
                            await taskProcessor(taskItem, cts.Token);
                        }
                        finally
                        {
                            if (c.TryRemove(taskItem.UniqueKey, out var removed))
                            {
                                Interlocked.Add(ref queuedBytes, -taskItem.GetTaskSizeInMemory());
                            }
                            (taskItem as IDisposable)?.Dispose();
                        }
                    }));
            }

            return TaskEnqueueResult.Enqueued;
        }

        /// <summary>
        /// Awaits all current tasks (more tasks can be added while this is running)
        /// </summary>
        /// <returns></returns>
        public Task AwaitAllCurrentTasks()
        {
            var tasks = c.Values.Select(w => w.GetTask()).Where(w => w != null).Cast<Task>().ToArray();
            return Task.WhenAll(tasks);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            stopped = true;
            lock (syncStopping)
            {
                cts.Cancel();
            }
            await AwaitAllCurrentTasks();
            foreach (var item in c.Values)
            {
                (item as IDisposable)?.Dispose();
            }
            c.Clear();
        }
    }
}
