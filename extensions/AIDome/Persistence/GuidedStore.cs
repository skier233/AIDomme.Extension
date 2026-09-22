using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIDome.Data;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AIDome.Persistence;

public sealed class GuidedStore(string connectionString, string legacyPath, string? originalLegacyPath = null)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() },
    };
    public const int LockNamespace = 0x41494447;
    public const string ImportSql = """
        CREATE TABLE aidomme_guided_imports ("Source" text PRIMARY KEY, "ImportedAtUtc" timestamptz NOT NULL);
        """;

    internal CommandCenterDbContext Context(int owner) => new(new DbContextOptionsBuilder<CommandCenterDbContext>()
        .UseNpgsql(connectionString)
        .ReplaceService<IModelCacheKeyFactory, GuidedModelCacheKeyFactory>().Options, owner);

    public async Task ImportAsync(CancellationToken ct)
    {
        await using var target = Context(0);
        await using var transaction = await target.Database.BeginTransactionAsync(ct);
        await target.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockNamespace}, 0)", ct);
        // A fixed source identity avoids replaying a development SQLite file over newer defaults.
        var imported = await target.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM aidomme_guided_imports WHERE \"Source\" = 'legacy-definitions-v1'").AnyAsync(ct);
        if (imported) return;
        var seedMethods = await target.StimMethods.AsNoTracking().ToListAsync(ct);
        var seedEnders = await target.PunishmentEnders.AsNoTracking().ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(originalLegacyPath) && !File.Exists(originalLegacyPath))
            throw new InvalidOperationException("The configured original AI Domme definition source does not exist.");
        if (!string.IsNullOrWhiteSpace(originalLegacyPath))
            await ImportFileAsync(originalLegacyPath, target, null, null, ct);
        if (File.Exists(legacyPath))
            await ImportFileAsync(legacyPath, target, originalLegacyPath is null ? null : seedMethods, originalLegacyPath is null ? null : seedEnders, ct);
        await target.SaveChangesAsync(ct);
        foreach (var type in GuidedSchema.DefinitionTypes)
        {
            var table = "aidomme_guided_" + type.Name.ToLowerInvariant();
            // Identifier derives exclusively from the fixed definition type allowlist.
            var sequenceSql = $"SELECT setval(pg_get_serial_sequence('{table}', 'Id'), GREATEST(COALESCE((SELECT MAX(\"Id\") FROM {table}), 0), 1), EXISTS(SELECT 1 FROM {table}));";
            await target.Database.ExecuteSqlRawAsync(sequenceSql, ct);
        }
        await target.Database.ExecuteSqlRawAsync("INSERT INTO aidomme_guided_imports (\"Source\", \"ImportedAtUtc\") VALUES ('legacy-definitions-v1', CURRENT_TIMESTAMP)", ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task ImportFileAsync(string path, CommandCenterDbContext target, List<StimMethod>? seedMethods, List<PunishmentEnder>? seedEnders, CancellationToken ct)
    {
        await using var source = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()).Options);
        // Explicit allowlist: never query or import sessions, usage, infractions or assigned punishments.
        await ImportTypeAsync(source, target, seedMethods, ct);
        await ImportTypeAsync(source, target, seedEnders, ct);
        await ImportTypeAsync<TuningRatingEntry>(source, target, null, ct);
        await ImportTypeAsync<TuningComparisonEntry>(source, target, null, ct);
        await target.SaveChangesAsync(ct);
    }

    private static async Task ImportTypeAsync<T>(CommandCenterDbContext source, CommandCenterDbContext target, List<T>? baseline, CancellationToken ct) where T : class, new()
    {
        var existing = await target.Set<T>().ToListAsync(ct);
        foreach (var row in await ReadLegacyDefinitionsAsync<T>(source, ct))
        {
            // SQLite loses DateTime.Kind. These legacy calibration fields are documented UTC.
            foreach (var property in typeof(T).GetProperties().Where(x => x.CanWrite && (x.PropertyType == typeof(DateTime) || x.PropertyType == typeof(DateTime?))))
                if (property.GetValue(row) is DateTime time)
                {
                    var utc = time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime();
                    property.SetValue(row, new DateTime(utc.Ticks / 10 * 10, DateTimeKind.Utc));
                }
            var id = (int)typeof(T).GetProperty("Id")!.GetValue(row)!;
            var current = existing.FirstOrDefault(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)! == id);
            if (row is TuningRatingEntry rating)
            {
                current = existing.FirstOrDefault(x => ((TuningRatingEntry)(object)x).CandidateKey == rating.CandidateKey);
                if (current is null && existing.Any(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)! == id))
                    rating.Id = existing.Max(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)!) + 1;
                else if (current is not null) rating.Id = ((TuningRatingEntry)(object)current).Id;
            }
            if (row is TuningComparisonEntry comparison)
            {
                if (existing.Any(x => {
                    var c = (TuningComparisonEntry)(object)x;
                    return c.Mode == comparison.Mode && c.WinnerKey == comparison.WinnerKey && c.LoserKey == comparison.LoserKey
                        && c.Strength == comparison.Strength && c.TimestampUtc == comparison.TimestampUtc;
                })) continue;
                current = null;
                if (existing.Any(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)! == id))
                    comparison.Id = existing.Max(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)!) + 1;
            }
            if (current is null) { target.Add(row); existing.Add(row); }
            else
            {
                var seed = baseline?.FirstOrDefault(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)! == id);
                // Unedited extension seed fields must not erase original learned multipliers.
                foreach (var property in target.Entry(current).Metadata.GetProperties().Where(x => x.PropertyInfo is not null && x.Name is not ("OwnerId" or "Id")))
                    if (seed is null || !Equals(property.PropertyInfo!.GetValue(seed), property.PropertyInfo!.GetValue(row)))
                        target.Entry(current).Property(property.Name).CurrentValue = property.PropertyInfo!.GetValue(row);
            }
        }
    }

    private static async Task<List<T>> ReadLegacyDefinitionsAsync<T>(CommandCenterDbContext source, CancellationToken ct) where T : class, new()
    {
        // Read only existing scalar columns so older definition schemas need no in-place migration.
        var entity = source.Model.FindEntityType(typeof(T))!;
        var table = entity.GetTableName()!;
        var connection = source.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table";
        var parameter = exists.CreateParameter(); parameter.ParameterName = "@table"; parameter.Value = table; exists.Parameters.Add(parameter);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 0) return [];
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\"";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var columns = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, x => x, StringComparer.OrdinalIgnoreCase);
        var results = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            var row = new T();
            foreach (var property in entity.GetProperties().Where(x => x.PropertyInfo is not null))
            {
                if (!columns.TryGetValue(property.GetColumnName(), out var ordinal) || reader.IsDBNull(ordinal)) continue;
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var raw = reader.GetValue(ordinal);
                var value = type.IsEnum ? Enum.ToObject(type, Convert.ToInt32(raw)) : type == typeof(DateTime)
                    ? DateTime.Parse(Convert.ToString(raw)!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
                    : Convert.ChangeType(raw, type, System.Globalization.CultureInfo.InvariantCulture);
                property.PropertyInfo!.SetValue(row, value);
            }
            results.Add(row);
        }
        return results;
    }

    internal async Task ApplyDefaultsAsync(CommandCenterDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockNamespace}, {db.OwnerId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO aidomme_guided_accounts (\"OwnerId\", \"UserId\") VALUES ({db.OwnerId}, {db.OwnerId}) ON CONFLICT DO NOTHING", ct);
        db.ApplyingDefaults = true;
        try
        {
            await CopyDefaultsAsync<StimMethod>(db, ct);
            await CopyDefaultsAsync<PunishmentEnder>(db, ct);
            await CopyDefaultsAsync<TuningRatingEntry>(db, ct);
            await CopyDefaultsAsync<TuningComparisonEntry>(db, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally { db.ApplyingDefaults = false; db.ChangeTracker.Clear(); }
    }

    private static async Task CopyDefaultsAsync<T>(CommandCenterDbContext db, CancellationToken ct) where T : class, new()
    {
        var defaults = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(x => EF.Property<int>(x, "OwnerId") == 0).ToListAsync(ct);
        var personal = await db.Set<T>().ToListAsync(ct);
        foreach (var shared in defaults)
        {
            var id = (int)typeof(T).GetProperty("Id")!.GetValue(shared)!;
            var effective = personal.FirstOrDefault(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)! == id);
            if (effective is null)
            {
                effective = new T();
                CopyScalars(db, effective, shared);
                db.Entry(effective).Property("OwnerId").CurrentValue = db.OwnerId;
                db.Add(effective);
            }
            else if (!(bool)db.Entry(effective).Property("IsOverride").CurrentValue!)
                CopyScalars(db, effective, shared);
        }
    }

    internal static void CopyScalars<T>(CommandCenterDbContext db, T target, T source) where T : class
    {
        var entry = db.Entry(target);
        foreach (var property in entry.Metadata.GetProperties().Where(x => x.PropertyInfo is not null && x.Name != "OwnerId"))
            entry.Property(property.Name).CurrentValue = property.PropertyInfo!.GetValue(source);
    }

    public BoundGuidedFactory ForUser(int userId)
        => userId > 0 ? new(this, userId) : throw new Cove.Core.Auth.UnauthorizedException("Sign in to use AI Domme.");
}

public sealed class BoundGuidedFactory(GuidedStore store, int userId) : IDbContextFactory<CommandCenterDbContext>
{
    public int UserId => userId;
    public CommandCenterDbContext CreateDbContext() => CreateDbContextAsync().GetAwaiter().GetResult();
    public async Task<CommandCenterDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var db = store.Context(userId);
        try { await store.ApplyDefaultsAsync(db, cancellationToken); return db; }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<CommandCenterDbContext> InTrackerTransactionAsync(CoveContext tracker, CancellationToken ct)
    {
        var db = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>()
            .UseNpgsql(tracker.Database.GetDbConnection())
            .ReplaceService<IModelCacheKeyFactory, GuidedModelCacheKeyFactory>().Options, userId);
        await db.Database.UseTransactionAsync(tracker.Database.CurrentTransaction!.GetDbTransaction(), ct);
        return db;
    }

    public async Task<TrackerDecisionMetrics> ReadTrackerMetricsAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var db = store.Context(userId);
        var day = nowUtc.Date;
        var end = day.AddDays(1);
        return await db.Database.SqlQuery<TrackerDecisionMetrics>($"""
            WITH facts AS (SELECT "Kind", "OccurredAtUtc" FROM aidomme_tracker_events WHERE "UserId" = {userId} AND "VoidedAtUtc" IS NULL),
              latest AS (SELECT MAX("OccurredAtUtc") AS last FROM facts WHERE "Kind" = 'orgasm')
            SELECT (SELECT last FROM latest) AS "LastOrgasmAtUtc",
              COUNT(*) FILTER (WHERE "Kind" = 'edge' AND ((SELECT last FROM latest) IS NULL OR "OccurredAtUtc" > (SELECT last FROM latest)))::integer AS "EdgesSinceLastOrgasm",
              COUNT(*) FILTER (WHERE "Kind" = 'edge' AND "OccurredAtUtc" >= {day} AND "OccurredAtUtc" < {end})::integer AS "EdgesToday"
            FROM facts
            """).SingleAsync(ct);
    }
}

public sealed class TrackerDecisionMetrics
{
    public DateTime? LastOrgasmAtUtc { get; set; }
    public int EdgesSinceLastOrgasm { get; set; }
    public int EdgesToday { get; set; }
}
