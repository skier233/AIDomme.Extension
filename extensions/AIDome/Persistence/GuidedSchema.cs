using System.Linq.Expressions;
using AIDome2025.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AIDome.Persistence;

/// <summary>One set of Cove-database tables; composite keys and foreign keys enforce account ownership.</summary>
public static class GuidedSchema
{
    public static readonly Type[] DefinitionTypes = [typeof(StimMethod), typeof(PunishmentEnder), typeof(TuningRatingEntry), typeof(TuningComparisonEntry)];
    public static bool IsDefinition(Type type) => DefinitionTypes.Contains(type);

    public static void ConfigureOwned(ModelBuilder model, CommandCenterDbContext context)
    {
        model.Entity<GuidedEventReceipt>().HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
        model.Entity<GuidedEventReceipt>().HasIndex(x => x.EventId).IsUnique();
        var types = model.Model.GetEntityTypes().ToArray();
        var relations = types.SelectMany(x => x.GetForeignKeys()).Select(x => new
        {
            Dependent = x.DeclaringEntityType.ClrType, Principal = x.PrincipalEntityType.ClrType,
            Columns = x.Properties.Select(p => p.Name).ToArray(),
            Navigation = x.DependentToPrincipal?.Name, Inverse = x.PrincipalToDependent?.Name, x.DeleteBehavior,
            Metadata = x,
        }).ToArray();
        foreach (var relation in relations) relation.Metadata.DeclaringEntityType.RemoveForeignKey(relation.Metadata);
        foreach (var type in types)
        {
            var builder = model.Entity(type.ClrType);
            var table = "aidomme_guided_" + type.ClrType.Name.ToLowerInvariant();
            builder.ToTable(table, t => t.HasCheckConstraint("CK_" + table + "_owner", IsDefinition(type.ClrType) ? "\"OwnerId\" >= 0" : "\"OwnerId\" > 0"));
            builder.Property<int>("OwnerId").HasDefaultValue(0).ValueGeneratedNever();
            builder.HasKey("OwnerId", "Id");
            builder.Property<int>("Id").ValueGeneratedOnAdd();
            if (IsDefinition(type.ClrType)) builder.Property<bool>("IsOverride").HasDefaultValue(false);
            foreach (var index in type.GetIndexes().ToArray())
            {
                var columns = index.Properties.Select(x => x.Name).ToArray();
                var unique = index.IsUnique;
                type.RemoveIndex(index);
                builder.HasIndex(new[] { "OwnerId" }.Concat(columns).ToArray()).IsUnique(unique);
            }
            var parameter = Expression.Parameter(type.ClrType, "row");
            var owner = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(int)], parameter, Expression.Constant("OwnerId"));
            builder.HasQueryFilter(Expression.Lambda(Expression.Equal(owner, Expression.Property(Expression.Constant(context), nameof(context.OwnerId))), parameter));
        }
        foreach (var relation in relations)
        {
            model.Entity(relation.Dependent).HasOne(relation.Principal, relation.Navigation).WithMany(relation.Inverse)
                .HasForeignKey(new[] { "OwnerId" }.Concat(relation.Columns).ToArray())
                .HasPrincipalKey("OwnerId", "Id")
                // SET NULL would null the nonnullable ownership component; definitions are never deleted.
                .OnDelete(relation.DeleteBehavior is DeleteBehavior.SetNull or DeleteBehavior.Restrict ? DeleteBehavior.NoAction : relation.DeleteBehavior);
        }
    }

    public static string CreateSql()
    {
        using var db = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .ReplaceService<IModelCacheKeyFactory, GuidedModelCacheKeyFactory>().Options);
        var sql = db.Database.GenerateCreateScript();
        sql += """
            CREATE TABLE aidomme_guided_accounts (
              "OwnerId" integer PRIMARY KEY, "UserId" integer UNIQUE REFERENCES users("Id") ON DELETE CASCADE,
              CHECK (("OwnerId" = 0 AND "UserId" IS NULL) OR ("OwnerId" > 0 AND "OwnerId" = "UserId")));
            INSERT INTO aidomme_guided_accounts ("OwnerId", "UserId") VALUES (0, NULL);
            """;
        foreach (var type in db.Model.GetEntityTypes())
        {
            var table = type.GetTableName()!;
            sql += $"\nALTER TABLE {table} ADD CONSTRAINT \"FK_{table}_account\" FOREIGN KEY (\"OwnerId\") REFERENCES aidomme_guided_accounts(\"OwnerId\") ON DELETE CASCADE;\n";
        }
        return sql;
    }
}

public sealed class GuidedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => (context.GetType(), context.Database.ProviderName, designTime);
}
