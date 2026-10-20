namespace AgentConnector;

public sealed class ConnectionCoordinator : IDisposable
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 257).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly long[] versions = new long[257];

    public async ValueTask<IDisposable> LockAsync(ConnectionKey key, CancellationToken cancellationToken)
    {
        var gate = gates[Index(key)];
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    public long Version(ConnectionKey key) => versions[Index(key)];
    public long Invalidate(ConnectionKey key) => ++versions[Index(key)];
    private static int Index(ConnectionKey key) => (int)((uint)key.GetHashCode() % 257);
    public void Dispose() { foreach (var gate in gates) gate.Dispose(); }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) gate.Release(); }
    }
}