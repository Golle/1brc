using Microsoft.Win32.SafeHandles;

namespace Challenge;
internal unsafe class WorkerPool : IDisposable
{
    private readonly Thread[] _threads;
    private WorkerPool(Thread[] threads)
        => _threads = threads;

    public void Join()
    {
        foreach (var thread in _threads)
        {
            thread.Join();
        }
    }

    public void Dispose()
        => Join();

    public static WorkerPool CreateAndExcute(DataContext[] contexts, BumpAllocator* allocator, delegate*<int, Span<DataContext>, BumpAllocator*, void> processChunk)
    {
        var threads = new Thread[contexts.Length];
        for (var i = 0; i < contexts.Length; ++i)
        {
            threads[i] = new Thread(index => processChunk((int)index!, contexts, allocator))
            {
                Priority = ThreadPriority.AboveNormal,
                IsBackground = true
            };
            threads[i].Start(i);
        }

        return new WorkerPool(threads);
    }
}
