using System.Collections.Concurrent;

public class FifoSemaphore
{
    private SemaphoreSlim semaphore;
    private ConcurrentQueue<TaskCompletionSource<bool>> queue =
        new ConcurrentQueue<TaskCompletionSource<bool>>();

    public FifoSemaphore(int initialCount)
    {
        semaphore = new SemaphoreSlim(initialCount);
    }

    public FifoSemaphore(int initialCount, int maxCount)
    {
        semaphore = new SemaphoreSlim(initialCount, maxCount);
    }

    public void Wait()
    {
        WaitAsync().Wait();
    }

    public Task WaitAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        queue.Enqueue(tcs);
        semaphore
            .WaitAsync()
            .ContinueWith(t =>
            {
                TaskCompletionSource<bool> popped;
                if (queue.TryDequeue(out popped))
                    popped.SetResult(true);
            });
        return tcs.Task;
    }

    public void Release()
    {
        semaphore.Release();
    }
}
