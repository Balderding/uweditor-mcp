using Xunit;

// Damage.Run watches the heap while a damaged file is read: other tests running beside it would
// count as its memory.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
