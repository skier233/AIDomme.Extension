using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record CommitmentDefinition(Guid Id, string Name, string Terms, string Unit, decimal Target, int? DueAfterDays, bool Enabled = true);
public sealed record StructuredOffer(Guid Id, Guid DefinitionId, string Name, string Terms, string Unit, decimal Target,
    int? DueAfterDays, DateTime? DueAtUtc, string Status, int? OriginSessionId, DateTime CreatedAtUtc, DateTime? AcceptedAtUtc = null);
public sealed record AcceptedCommitment(Guid Id, Guid OfferId, string Name, string Terms, string Unit, decimal Target,
    decimal Progress, DateTime? DueAtUtc, string Status, DateTime AcceptedAtUtc, DateTime? ResolvedAtUtc = null);
public sealed record CommitmentReport(Guid Id, Guid CommitmentId, string Outcome, int Degree, string? Note, DateTime ReportedAtUtc);
public sealed class CommitmentDocument
{
    public int Format { get; set; } = 1;
    public long Revision { get; set; }
    public List<CommitmentDefinition> Definitions { get; set; } = [];
    public List<StructuredOffer> Offers { get; set; } = [];
    public List<AcceptedCommitment> Commitments { get; set; } = [];
    public List<CommitmentReport> Reports { get; set; } = [];
    public Dictionary<Guid, string> Operations { get; set; } = [];
}
public sealed record CommitmentMutation(Guid EventId, long ExpectedRevision, string Action, Guid? TargetId = null,
    CommitmentDefinition? Definition = null, decimal? Amount = null, string? Outcome = null, int? Degree = null,
    string? Note = null, int? OriginSessionId = null);

public sealed class CommitmentStore(GuidedRuntimes runtimes)
{
    private BoundGuidedFactory Factory => runtimes.Current.Factory;
    public const string Sql = """
        CREATE TABLE aidomme_commitment_state (
          "OwnerId" integer PRIMARY KEY REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
          "Revision" bigint NOT NULL, "Payload" text NOT NULL, "UpdatedAtUtc" timestamptz NOT NULL,
          CHECK ("OwnerId" > 0));
        """;

    public Task<CommitmentDocument> GetAsync(CancellationToken ct) => LoadAsync(ct);

    public async Task<CommitmentDocument> MutateAsync(CommitmentMutation request, CancellationToken ct)
    {
        if (request.EventId == Guid.Empty) throw new ArgumentException("An event ID is required.");
        if (string.IsNullOrWhiteSpace(request.Action)) throw new ArgumentException("An action is required.");
        await using var db = await Factory.CreateDbContextAsync(ct);
        var owner = Factory.UserId;
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var state = await ReadAsync(connection, transaction, owner, true, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, GuidedStore.Json))));
        if (state.Operations.TryGetValue(request.EventId, out var prior))
        {
            if (prior != fingerprint) throw new ArgumentException("That event ID was already used for a different request.");
            await transaction.CommitAsync(ct);
            return state;
        }
        if (request.ExpectedRevision != state.Revision) throw new InvalidOperationException("Commitment data changed in another view. Refresh and try again.");
        Apply(state, request);
        state.Operations[request.EventId] = fingerprint;
        state.Revision++;
        var payload = JsonSerializer.Serialize(state, GuidedStore.Json);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO aidomme_commitment_state ("OwnerId", "Revision", "Payload", "UpdatedAtUtc")
            VALUES (@owner, @revision, @payload, CURRENT_TIMESTAMP)
            ON CONFLICT ("OwnerId") DO UPDATE SET "Revision"=@revision, "Payload"=@payload, "UpdatedAtUtc"=CURRENT_TIMESTAMP
            """;
        Add(command, "owner", owner); Add(command, "revision", state.Revision); Add(command, "payload", payload);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return state;
    }

    private static void Apply(CommitmentDocument state, CommitmentMutation request)
    {
        var now = DateTime.UtcNow;
        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "save-definition":
                var definition = request.Definition ?? throw new ArgumentException("A definition is required.");
                Validate(definition);
                var id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id;
                var normalized = definition with { Id = id, Name = definition.Name.Trim(), Terms = definition.Terms.Trim(), Unit = definition.Unit.Trim() };
                state.Definitions.RemoveAll(x => x.Id == id); state.Definitions.Add(normalized); break;
            case "delete-definition":
                var definitionId = Required(request.TargetId);
                if (state.Offers.Any(x => x.DefinitionId == definitionId)) throw new InvalidOperationException("Definitions used by an offer are retained for its audit trail. Disable it instead.");
                if (state.Definitions.RemoveAll(x => x.Id == definitionId) == 0) throw new ArgumentException("Definition was not found."); break;
            case "propose":
                var source = state.Definitions.SingleOrDefault(x => x.Id == Required(request.TargetId)) ?? throw new ArgumentException("Definition was not found.");
                if (!source.Enabled) throw new InvalidOperationException("That definition is disabled.");
                state.Offers.Add(new(Guid.NewGuid(), source.Id, source.Name, source.Terms, source.Unit, source.Target,
                    source.DueAfterDays, null, "Proposed", request.OriginSessionId, now)); break;
            case "accept":
                var offer = Proposed(state, request.TargetId);
                DateTime? dueAt = offer.DueAfterDays is int days ? now.AddDays(days) : null;
                state.Offers[state.Offers.IndexOf(offer)] = offer with { Status = "Accepted", AcceptedAtUtc = now, DueAtUtc = dueAt };
                state.Commitments.Add(new(Guid.NewGuid(), offer.Id, offer.Name, offer.Terms, offer.Unit, offer.Target, 0,
                    dueAt, "Active", now)); break;
            case "decline":
            case "cancel-offer":
                var rejected = Proposed(state, request.TargetId);
                state.Offers[state.Offers.IndexOf(rejected)] = rejected with { Status = request.Action.StartsWith("decline") ? "Declined" : "Cancelled" }; break;
            case "progress":
                var current = Active(state, request.TargetId); var amount = request.Amount ?? throw new ArgumentException("An amount is required.");
                if (amount <= 0 || amount > 1_000_000) throw new ArgumentException("Progress must be greater than zero and no more than 1,000,000.");
                state.Commitments[state.Commitments.IndexOf(current)] = current with { Progress = current.Progress + amount }; break;
            case "report":
                var commitment = Active(state, request.TargetId);
                var outcome = request.Outcome?.Trim() ?? "";
                if (outcome is not ("Completed" or "Partial" or "Broken")) throw new ArgumentException("Outcome must be Completed, Partial, or Broken.");
                var degree = request.Degree ?? (outcome == "Completed" ? 100 : 0);
                if (degree is < 0 or > 100) throw new ArgumentException("Degree must be between 0 and 100.");
                if (request.Note?.Length > 1000) throw new ArgumentException("Report notes are limited to 1,000 characters.");
                state.Commitments[state.Commitments.IndexOf(commitment)] = commitment with { Status = outcome, ResolvedAtUtc = now };
                state.Reports.Add(new(Guid.NewGuid(), commitment.Id, outcome, degree, request.Note?.Trim(), now)); break;
            default: throw new ArgumentException("Unknown commitment action.");
        }
    }
    private static void Validate(CommitmentDefinition item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100) throw new ArgumentException("Name is required and limited to 100 characters.");
        if (string.IsNullOrWhiteSpace(item.Terms) || item.Terms.Length > 2000) throw new ArgumentException("Terms are required and limited to 2,000 characters.");
        if (string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 40) throw new ArgumentException("A unit is required and limited to 40 characters.");
        if (item.Target <= 0 || item.Target > 1_000_000) throw new ArgumentException("Target must be greater than zero and no more than 1,000,000.");
        if (item.DueAfterDays is < 0 or > 3650) throw new ArgumentException("Due days must be between 0 and 3,650.");
    }
    private static Guid Required(Guid? id) => id is { } value && value != Guid.Empty ? value : throw new ArgumentException("A target ID is required.");
    private static StructuredOffer Proposed(CommitmentDocument state, Guid? id) => state.Offers.SingleOrDefault(x => x.Id == Required(id) && x.Status == "Proposed") ?? throw new InvalidOperationException("Offer is no longer pending.");
    private static AcceptedCommitment Active(CommitmentDocument state, Guid? id) => state.Commitments.SingleOrDefault(x => x.Id == Required(id) && x.Status == "Active") ?? throw new InvalidOperationException("Commitment is no longer active.");
    private async Task<CommitmentDocument> LoadAsync(CancellationToken ct)
    {
        await using var db = await Factory.CreateDbContextAsync(ct); var owner = Factory.UserId;
        var connection = db.Database.GetDbConnection(); if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        return await ReadAsync(connection, null, owner, false, ct);
    }
    private static async Task<CommitmentDocument> ReadAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction? transaction, int owner, bool locked, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT \"Payload\" FROM aidomme_commitment_state WHERE \"OwnerId\"=@owner{(locked ? " FOR UPDATE" : "")}"; Add(command, "owner", owner);
        var value = await command.ExecuteScalarAsync(ct); if (value is null or DBNull) return new();
        var result = JsonSerializer.Deserialize<CommitmentDocument>((string)value, GuidedStore.Json) ?? throw new InvalidOperationException("Invalid commitment state.");
        if (result.Format != 1) throw new InvalidOperationException("Unsupported commitment state version."); return result;
    }
    private static void Add(System.Data.Common.DbCommand command, string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
}
