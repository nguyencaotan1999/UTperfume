using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>What happened to the request body of one request: bytes handed to a reader, and whether the form was parsed.</summary>
public sealed record RequestReadRecord(string Method, string Path, long BodyBytesRead, bool FormParsed);

/// <summary>
/// Test-host probe, outermost in the pipeline: wraps the request body in a counting stream and, after the request,
/// records whether any byte was read and whether <see cref="IFormFeature.Form"/> was populated. It proves that a rejected
/// request was rejected before its body or form was read (M1, M2, M3).
/// </summary>
public sealed class RequestReadProbe
{
    private readonly ConcurrentQueue<RequestReadRecord> _records = new();

    public IReadOnlyList<RequestReadRecord> Records => [.. _records];

    public RequestReadRecord Last(string method, string path) =>
        _records.Last(r => r.Method == method && r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    internal void Add(RequestReadRecord record) => _records.Enqueue(record);

    /// <summary>Registers the probe and its outermost middleware in a test host.</summary>
    public void Install(IServiceCollection services)
    {
        services.AddTransient<IStartupFilter>(_ => new ProbeStartupFilter(this));
    }

    private sealed class ProbeStartupFilter(RequestReadProbe probe) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    var counting = new CountingStream(context.Request.Body);
                    context.Request.Body = counting;
                    try
                    {
                        await nextMiddleware(context);
                    }
                    finally
                    {
                        var form = context.Features.Get<IFormFeature>()?.Form;
                        probe.Add(new RequestReadRecord(
                            context.Request.Method, context.Request.Path.Value ?? string.Empty, counting.BytesRead, form is not null));
                    }
                });
                next(app);
            };
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        private long _bytesRead;

        public long BytesRead => Interlocked.Read(ref _bytesRead);

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            Interlocked.Add(ref _bytesRead, read);
            return read;
        }
    }
}
