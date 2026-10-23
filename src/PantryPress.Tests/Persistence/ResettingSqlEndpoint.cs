using System.Net;
using System.Net.Sockets;

namespace PantryPress.Tests.Persistence;

// A loopback endpoint that accepts every connection and resets it at once. SqlClient sees a transport error
// (10054), which the SQL Server execution strategy treats as transient, so it retries.
internal sealed class ResettingSqlEndpoint : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public ResettingSqlEndpoint()
    {
        _listener.Start();
        _loop = AcceptAndResetAsync(_stop.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptAndResetAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            client.Client.LingerState = new LingerOption(true, 0);
        }
    }
}
