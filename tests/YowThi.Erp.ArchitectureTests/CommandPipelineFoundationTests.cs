using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.ArchitectureTests;

public sealed class CommandPipelineFoundationTests
{
    [Fact]
    public void Request_hash_requires_exact_sha256_length()
    {
        Assert.Throws<ArgumentException>(() =>
            CommandRequestHash.FromSha256(new byte[31]));

        Assert.NotNull(CommandRequestHash.FromSha256(new byte[32]));
    }

    [Fact]
    public void Json_payloads_validate_json_and_object_shape()
    {
        var payload = JsonPayload.FromUtf8Json("{\"value\":1}"u8);
        var subjectKey = JsonObjectPayload.FromUtf8Json("{\"id\":\"abc\"}"u8);

        Assert.NotEmpty(payload.Utf8Json.ToArray());
        Assert.NotEmpty(subjectKey.Utf8Json.ToArray());
        Assert.Throws<ArgumentException>(() => JsonObjectPayload.FromUtf8Json("[]"u8));
    }

    [Fact]
    public void Registered_command_hasher_is_sha256_over_canonical_payload_bytes()
    {
        var services = new ServiceCollection();
        services.AddErpPersistence("Host=localhost;Database=yowthi_erp_v2;Username=postgres");

        using var provider = services.BuildServiceProvider();
        var hasher = provider.GetRequiredService<ICommandRequestHasher>();
        var payload = JsonPayload.FromUtf8Json("{\"a\":1}"u8);

        var actual = hasher.Compute(payload);
        var expected = SHA256.HashData(payload.Utf8Json.Span);

        Assert.True(actual.Bytes.Span.SequenceEqual(expected));
    }

    [Fact]
    public void Transaction_decision_explicitly_distinguishes_commit_from_rollback()
    {
        var commit = CommandTransactionDecision<int>.Commit(1);
        var rollback = CommandTransactionDecision<int>.Rollback(2);

        Assert.True(commit.ShouldCommit);
        Assert.Equal(1, commit.Value);
        Assert.False(rollback.ShouldCommit);
        Assert.Equal(2, rollback.Value);
    }
}
