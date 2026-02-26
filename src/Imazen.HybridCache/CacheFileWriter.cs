using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Imazen.Common.Concurrency;

namespace Imazen.HybridCache
{
    internal class CacheFileWriter
    {
        internal struct FileWriteResult
        {
            internal FileWriteStatus Status { get; set; }
            internal long BytesWritten { get; set; }
        }
        internal enum FileWriteStatus
        {
            FileCreated, 
            FileAlreadyExists,
            LockTimeout,
        }

        private readonly Action<string, string> moveFileOverwriteFunc;
        private AsyncLockProvider WriteLocks { get; }

        private readonly bool moveIntoPlace;
        public CacheFileWriter(AsyncLockProvider writeLocks, Action<string,string> moveFileOverwriteFunc, bool moveIntoPlace)
        {
            this.moveIntoPlace = moveIntoPlace;
            WriteLocks = writeLocks;
            this.moveFileOverwriteFunc = moveFileOverwriteFunc;
        }

        private void MoveOverwrite(string from, string to)
        {
            if (moveFileOverwriteFunc != null)
            {
                moveFileOverwriteFunc(from, to);
            }
            else
            {
#if NET5_0_OR_GREATER
                File.Move(from, to, true);
#else
                File.Move(from, to);
#endif
            }
        }


        /// <summary>
        /// Returns true if either (a) the file was written, or (b) the file already existed
        /// Returns false if the in-process lock failed. Throws an exception if any kind of file or processing exception occurs.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="writeCallback"></param>
        /// <param name="timeoutMs"></param>
        /// <param name="recheckFileSystemFirst"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task<FileWriteStatus> TryWriteFile(CacheEntry entry,
            Func<Stream, CancellationToken,Task> writeCallback, 
            bool recheckFileSystemFirst, 
            int timeoutMs, 
            CancellationToken cancellationToken)
        {
            var resultStatus = FileWriteStatus.FileAlreadyExists;
            
            // ReSharper disable once InvertIf
            if (recheckFileSystemFirst)
            {
                var miss = !File.Exists(entry.PhysicalPath);
                if (!miss && !WriteLocks.MayBeLocked(entry.StringKey)) return FileWriteStatus.FileAlreadyExists;
            }
            
            var lockingSucceeded = await WriteLocks.TryExecuteAsync(entry.StringKey, timeoutMs, cancellationToken,
                async () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    if (File.Exists(entry.PhysicalPath)) return;


                    var subdirectoryPath = Path.GetDirectoryName(entry.PhysicalPath);
                    //Create subdirectory if needed.
                    if (subdirectoryPath != null && !Directory.Exists(subdirectoryPath))
                    {
                        Directory.CreateDirectory(subdirectoryPath);
                    }

                    string writeToFile;
                    if (moveIntoPlace)
                    {

                        writeToFile = entry.PhysicalPath + ".tmp_" + new Random().Next(int.MaxValue).ToString("x") +
                                      ".tmp";
                    }
                    else
                    {
                        writeToFile = entry.PhysicalPath;
                    }


                    var fs = new FileStream(writeToFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                        FileOptions.Asynchronous);
                    var finished = false;
                    try
                    {
                        using (fs)
                        {
                            //Run callback to write the cached data
                            await writeCallback(fs, cancellationToken); //Can throw any number of exceptions.
                            await fs.FlushAsync(cancellationToken);
                            fs.Flush(true);
                        }

                        try
                        {
                            if (moveIntoPlace)
                            {
                                MoveOverwrite(writeToFile, entry.PhysicalPath);
                            }

                            resultStatus = FileWriteStatus.FileCreated;
                            finished = true;
                        }
                        //Will throw IO exception if already exists. Which we consider a hit, so we delete the tempFile
                        catch (IOException)
                        {
                        }
                    }
                    finally
                    {
                        //Don't leave half-written files around.
                        if (!finished)
                        {
                            try
                            {
                                if (File.Exists(writeToFile)) File.Delete(writeToFile);
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }


                });

            if (!lockingSucceeded)
            {
                return FileWriteStatus.LockTimeout;
            }
            return resultStatus;
        }
    }
}