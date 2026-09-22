using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIDome.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record CommitmentView(IReadOnlyList<OfferRecord> Offers, IReadOnlyList<CommitmentRecord> Commitments,
    IReadOnlyList<CommitmentReportRecord> Reports);

/// <summary>A user response to an offer or commitment. The event ID makes retries safe.</summary>
public sealed record CommitmentMutation(Guid EventId, string Action, Guid? TargetId = null, decimal? Amount = null,
    string? Outcome = null, int? Degree = null, string? Note = null);

/// <summary>
/// Offers the domme has put to the user and the commitments the user explicitly accepted. Offers
/// are created by the domme (<see cref="ProposeAsync"/>), never authored by the user. Only an
/// explicit acceptance creates a commitment; progress and the user's report are recorded separately.
/// </summary>
public sealed class CommitmentStore(GuidedRuntimes runtimes)
{
    public async Task<CommitmentView> GetAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        return await ViewAsync(db, ct);
    }

    /// <summary>Puts an offer to the user. Terms are snapshotted so later library edits cannot change it.</summary>
    public static async Task<OfferRecord> ProposeAsync(GuidedDataFactory factory, string name, string terms, string unit,
        decimal target, int? dueAfterDays, int? originSessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) throw new ArgumentException("An offer name is required and limited to 100 characters.");
        if (string.IsNullOrWhiteSpace(terms) || terms.Length > 2000) throw new ArgumentException("Offer terms are required and limited to 2,000 characters.");
        if (string.IsNullOrWhiteSpace(unit) || unit.Length > 40) throw new ArgumentException("A unit is required and limited to 40 characters.");
        if (target <= 0 || target > 1_000_000) throw new ArgumentException("Target must be greater than zero and no more than 1,000,000.");
        if (dueAfterDays is < 0 or > 3650) throw new ArgumentException("Due days must be between 0 and 3,650.");
        await using var db = factory.Open();
        var offer = db.Add(new OfferRecord
        {
            Id = Guid.NewGuid(), Name = name.Trim(), Terms = terms.Trim(), Unit = unit.Trim(), Target = target,
            DueAfterDays = dueAfterDays, Status = "Proposed", OriginSessionId = originSessionId, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return offer;
    }

    public async Task<CommitmentView> MutateAsync(CommitmentMutation request, CancellationToken ct)
    {
        if (request.EventId == Guid.Empty) throw new ArgumentException("An event ID is required.");
        if (string.IsNullOrWhiteSpace(request.Action)) throw new ArgumentException("An action is required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, GuidedRecovery.Json))));
        await using var db = runtimes.Current.Factory.Open();
        await db.InTransactionAsync(async () =>
        {
            // A concurrent duplicate waits on this row, then sees it as already applied.
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_commitment_operations ("UserId", "EventId", "Fingerprint")
                VALUES ({db.UserId}, {request.EventId}, {fingerprint}) ON CONFLICT DO NOTHING
                """, ct);
            if (claimed == 0)
            {
                var prior = await db.Database.SqlQuery<string>($"""
                    SELECT "Fingerprint" AS "Value" FROM aidomme_commitment_operations WHERE "UserId" = {db.UserId} AND "EventId" = {request.EventId}
                    """).SingleAsync(ct);
                if (prior != fingerprint) throw new ArgumentException("That event ID was already used for a different request.");
                return false;
            }
            await ApplyAsync(db, request, ct);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await ViewAsync(db, ct);
    }

    private static async Task ApplyAsync(GuidedData db, CommitmentMutation request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var target = request.TargetId is { } id && id != Guid.Empty ? id : throw new ArgumentException("A target ID is required.");
        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "accept":
            {
                var offer = await ProposedAsync(db, target, ct);
                offer.Status = "Accepted";
                offer.DecidedAtUtc = now;
                offer.DueAtUtc = offer.DueAfterDays is int days ? now.AddDays(days) : null;
                db.Add(new CommitmentRecord
                {
                    Id = Guid.NewGuid(), OfferId = offer.Id, Name = offer.Name, Terms = offer.Terms, Unit = offer.Unit,
                    Target = offer.Target, DueAtUtc = offer.DueAtUtc, Status = "Active", AcceptedAtUtc = now,
                });
                break;
            }
            case "decline":
            case "cancel-offer":
            {
                var offer = await ProposedAsync(db, target, ct);
                offer.Status = request.Action.Trim().Equals("decline", StringComparison.OrdinalIgnoreCase) ? "Declined" : "Cancelled";
                offer.DecidedAtUtc = now;
                break;
            }
            case "progress":
            {
                var commitment = await ActiveAsync(db, target, ct);
                var amount = request.Amount ?? throw new ArgumentException("An amount is required.");
                if (amount <= 0 || amount > 1_000_000) throw new ArgumentException("Progress must be greater than zero and no more than 1,000,000.");
                commitment.Progress += amount;
                break;
            }
            case "report":
            {
                var commitment = await ActiveAsync(db, target, ct);
                var outcome = request.Outcome?.Trim() ?? "";
                if (outcome is not ("Completed" or "Partial" or "Broken")) throw new ArgumentException("Outcome must be Completed, Partial, or Broken.");
                var degree = request.Degree ?? (outcome == "Completed" ? 100 : 0);
                if (degree is < 0 or > 100) throw new ArgumentException("Degree must be between 0 and 100.");
                if (request.Note?.Length > 1000) throw new ArgumentException("Report notes are limited to 1,000 characters.");
                commitment.Status = outcome;
                commitment.ResolvedAtUtc = now;
                db.Add(new CommitmentReportRecord
                {
                    Id = Guid.NewGuid(), CommitmentId = commitment.Id, Outcome = outcome, Degree = degree,
                    Note = request.Note?.Trim(), ReportedAtUtc = now,
                });
                break;
            }
            default:
                throw new ArgumentException("Unknown commitment action.");
        }
    }

    private static async Task<OfferRecord> ProposedAsync(GuidedData db, Guid id, CancellationToken ct)
        => await db.Db.Set<OfferRecord>().FromSql($"""
               SELECT * FROM aidomme_offers WHERE "Id" = {id} AND "UserId" = {db.UserId} AND "Status" = 'Proposed' FOR UPDATE
               """).SingleOrDefaultAsync(ct)
           ?? throw new InvalidOperationException("Offer is no longer pending.");

    private static async Task<CommitmentRecord> ActiveAsync(GuidedData db, Guid id, CancellationToken ct)
        => await db.Db.Set<CommitmentRecord>().FromSql($"""
               SELECT * FROM aidomme_commitments WHERE "Id" = {id} AND "UserId" = {db.UserId} AND "Status" = 'Active' FOR UPDATE
               """).SingleOrDefaultAsync(ct)
           ?? throw new InvalidOperationException("Commitment is no longer active.");

    private static async Task<CommitmentView> ViewAsync(GuidedData db, CancellationToken ct) => new(
        await db.Offers.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync(ct),
        await db.Commitments.AsNoTracking().OrderBy(x => x.AcceptedAtUtc).ToListAsync(ct),
        await db.CommitmentReports.AsNoTracking().OrderBy(x => x.ReportedAtUtc).ToListAsync(ct));
}
