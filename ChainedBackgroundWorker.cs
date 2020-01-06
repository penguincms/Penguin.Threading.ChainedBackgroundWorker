using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Penguin.Threading
{
    /// <summary>
    /// The simplest way to launch a chained background instance 
    /// </summary>
    public static class ChainedBackgroundWorker
    {
        /// <summary>
        /// Creates the first chain link in the processing queue
        /// </summary>
        /// <typeparam name="T">The IEnumerable type to process</typeparam>
        /// <param name="toProcess">The IEnumerable to process</param>
        /// <returns>An instance representing the first block in the processing chain</returns>
        public static ChainedBackgroundWorker<T> Process<T>(IEnumerable<T> toProcess)
        {
            ConcurrentQueue<T> queue = new ConcurrentQueue<T>();

            foreach(T t in toProcess)
            {
                queue.Enqueue(t);
            }

            return new ChainedBackgroundWorker<T>(queue);
        }
    }

    /// <summary>
    /// First instance of a chain link in the processing queue, that processes items iteratively on its own thread and 
    /// passes them to the next link
    /// </summary>
    /// <typeparam name="TArgument">The type of the object to process</typeparam>
    public class ChainedBackgroundWorker<TArgument> : ChainedBackgroundWorker<TArgument, TArgument>
    {
        /// <summary>
        /// Is the link currently processing anything?
        /// </summary>
        public override bool IsBusy { get; set; }

        internal ChainedBackgroundWorker(ConcurrentQueue<TArgument> Source) : base(Source)
        {
            (this as IChainedBackgroundWorker<TArgument>).Queue = Source;
        }

        internal override void DoWork()
        {
            IsBusy = true;

            foreach (TArgument argument in (this as IChainedBackgroundWorker<TArgument>).Queue)
            {
                ((Child as IChainedBackgroundWorker<TArgument>).Queue as ConcurrentQueue<TArgument>).Enqueue(argument);
            }

            IsBusy = false;
            IsCompleted = true;

            if (!Child.IsBusy)
            {
                Child.DoWork();
            }


        }
    }

    /// <summary>
    /// A chain link representing both a source of, and destination to, a type being processed by the chain
    /// </summary>
    /// <typeparam name="TArgument"></typeparam>
    /// <typeparam name="TResult"></typeparam>
    public class ChainedBackgroundWorker<TArgument, TResult> : IChainedBackgroundWorker, IChainedBackgroundWorker<TArgument>
    {
        TaskCompletionSource<List<TResult>> ResultTask = new TaskCompletionSource<List<TResult>>();

        List<TResult> Results = new List<TResult>();

        /// <summary>
        /// Is this link processing anything currently?
        /// </summary>
        public virtual bool IsBusy
        {
            get
            {
                return InternalWorker.IsBusy;
            }
            set
            {
            }
        }

        /// <summary>
        /// Has this link (and all parents) finished processing their work?
        /// </summary>
        public bool IsCompleted { get; set; }

        IEnumerable<TArgument> IChainedBackgroundWorker<TArgument>.Queue { get; set; }

        /// <summary>
        /// Creates and returns another link in the processing chain
        /// </summary>
        /// <typeparam name="CResult">The output type of this chain link</typeparam>
        /// <param name="toRun">The function that gets run on each item passed into this link before it is passed to the next link</param>
        /// <returns>The chain link created by this process</returns>
        public ChainedBackgroundWorker<TResult, CResult> ContinueWith<CResult>(Func<TResult, CResult> toRun)
        {
            ChainedBackgroundWorker<TResult, CResult> child = new ChainedBackgroundWorker<TResult, CResult>(new ConcurrentQueue<TResult>())
            {
                Parent = this
            };

            child.SetFunction(toRun);

            if (this.Child is null)
            {
                this.Child = child;
            }
            else
            {
                throw new Exception("This worker already contains a child");
            }

            return child;
        }

        internal virtual void DoWork()
        {
            if (!Parent.IsBusy && !Parent.IsCompleted)
            {
                Parent.DoWork();
            }
             
            InternalWorker.RunWorkerAsync((this as IChainedBackgroundWorker<TArgument>).Queue as ConcurrentQueue<TArgument>);
        }

        void IChainedBackgroundWorker.DoWork() => DoWork();

        /// <summary>
        /// Kicks off the chain link of workers and returns a task whos result will contain the final post-process
        /// list
        /// </summary>
        /// <returns>A task whos result will contain the final post-process list</returns>
        public Task<List<TResult>> ExecuteAsync()
        {
            this.DoWork();

            return ResultTask.Task;
        }

        internal IChainedBackgroundWorker<TResult> Child { get; set; }

        internal IChainedBackgroundWorker Parent { get; set; }

        internal ChainedBackgroundWorker(ConcurrentQueue<TArgument> Source)
        {
            (this as IChainedBackgroundWorker<TArgument>).Queue = Source;
        }

        internal void SetFunction(Func<TArgument, TResult> toRun)
        {
            InternalWorker = BackgroundWorker.Create<ConcurrentQueue<TArgument>>((source, args) =>
            {
                while (args.TryDequeue(out TArgument next))
                {
                    TResult result = toRun.Invoke(next);

                    if (!(Child is null))
                    {

                        ((Child as IChainedBackgroundWorker<TResult>).Queue as ConcurrentQueue<TResult>).Enqueue(result);

                        if (!Child.IsBusy)
                        {
                            Child.DoWork();
                        }
                    }
                    else
                    {
                        Results.Add(result);
                    }

                }

                if (Parent.IsCompleted)
                {
                    this.IsCompleted = true;
                    ResultTask?.TrySetResult(this.Results);
                }

                Child?.DoWork();
            }); ;
        }

        private BackgroundWorker<ConcurrentQueue<TArgument>> InternalWorker { get; set; }
    }
}