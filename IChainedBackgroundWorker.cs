using System.Collections.Generic;

namespace Penguin.Threading
{
    internal interface IChainedBackgroundWorker
    {
        bool IsBusy { get; set; }
        bool IsCompleted { get; }
        void DoWork();
    }

    internal interface IChainedBackgroundWorker<TArgument> : IChainedBackgroundWorker
    {
        IEnumerable<TArgument> Queue { get; set; }
    }
}