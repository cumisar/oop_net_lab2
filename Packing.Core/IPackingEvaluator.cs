using System;
using System.Collections.Generic;
using System.Threading;

namespace Packing.Core {
    public sealed class PackingCandidate {
        public int Index { get; }
        public int[] Order { get; }
        public int StripWidth { get; }

        public PackingCandidate(int entryNumber, int[] tileOrder, int laneLimit) {
            Index = entryNumber;
            Order = (int[])tileOrder.Clone();
            StripWidth = laneLimit;
        }
    }

    public interface IPackingEvaluator {
        void Evaluate(PackingProblem packingTask, IReadOnlyList<PackingCandidate> genomeTasks, int parallelLimit, Action<int, PackingSolution> reportLayout, CancellationToken stopSignal);
    }
}
