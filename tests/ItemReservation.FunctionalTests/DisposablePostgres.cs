using System.Diagnostics;
using Npgsql;

namespace eShop.ItemReservation.FunctionalTests;

// A dedicated container on a random loopback port. Never connects to an application database.
internal sealed class DisposablePostgres : IAsyncDisposable
{
    private readonly string name = "eshop-reservation-tests-" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    public async Task StartAsync()
    {
        await DockerAsync("run", "--detach", "--rm", "--name", name,
            "--publish", "127.0.0.1::5432", "--env", "POSTGRES_PASSWORD=reservation-test-only",
            "--env", "POSTGRES_DB=reservation_tests", "pgvector/pgvector:pg17");
        var mapping = await DockerAsync("port", name, "5432/tcp");
        var port = int.Parse(mapping.Trim().Split(':')[^1]);
        ConnectionString = $"Host=127.0.0.1;Port={port};Database=reservation_tests;Username=postgres;Password=reservation-test-only;Include Error Detail=true";
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException) { await Task.Delay(500); }
        }
        throw new TimeoutException("Reservation test PostgreSQL did not start.");
    }

    public async ValueTask DisposeAsync() => await DockerAsync("rm", "--force", name);

    private static async Task<string> DockerAsync(params string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Docker.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await stderr);
        return await stdout;
    }
}
