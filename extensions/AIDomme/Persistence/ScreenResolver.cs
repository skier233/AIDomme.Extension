using System.Collections.Concurrent;
using AIDomme.Sessions;
using Cove.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIDomme.Persistence;

/// <summary>
/// Turns the page's report of what is on screen (a video and a position) into what she can talk
/// about: the title, the performers, the video's tags, and the tags of the part playing now. Reads
/// Cove's own tables through the request's context, so Cove's permission filters apply. A video's
/// metadata is cached for a few minutes; only the position changes from poll to poll.
/// </summary>
public sealed class ScreenResolver
{
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<int, (DateTime At, VideoFacts Facts)> cache = new();

    private sealed record Part(double Start, double? End, string[] Tags, string? Title);
    private sealed record VideoFacts(string? Title, string[] Performers, string[] Tags, List<Part> Parts);

    /// <summary>Parses the page's report: <c>video:12:345.6</c> (kind, id, position in seconds).</summary>
    public static (string Kind, int Id, double? Position)? Parse(string? media)
    {
        if (string.IsNullOrWhiteSpace(media)) return null;
        var parts = media.Split(':');
        if (parts.Length < 2 || parts[0] is not ("video" or "image") || !int.TryParse(parts[1], out var id) || id <= 0) return null;
        double? position = parts.Length > 2 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;
        return (parts[0], id, position);
    }

    public async Task<Screen?> ResolveAsync(DbContext db, string kind, int id, double? position, DateTime now, CancellationToken ct)
    {
        if (kind != "video") return new() { Kind = kind, Id = id, SeenAtUtc = now };
        if (!cache.TryGetValue(id, out var cached) || now - cached.At > Keep)
        {
            cached = (now, await LoadAsync(db, id, ct));
            cache[id] = cached;
            if (cache.Count > 200) foreach (var old in cache.Where(x => now - x.Value.At > Keep).Select(x => x.Key).ToList()) cache.TryRemove(old, out _);
        }
        var facts = cached.Facts;
        var here = position is double at ? facts.Parts.Where(p => p.Start <= at && (p.End ?? p.Start + 30) >= at).ToList() : [];
        return new()
        {
            Kind = kind, Id = id, Position = position, Title = facts.Title, Performers = facts.Performers, Tags = facts.Tags,
            NowTags = here.SelectMany(p => p.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NowTitle = here.Select(p => p.Title).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            SeenAtUtc = now,
        };
    }

    private static async Task<VideoFacts> LoadAsync(DbContext db, int id, CancellationToken ct)
    {
        var video = await db.Set<Video>().AsNoTracking().Where(v => v.Id == id)
            .Select(v => new
            {
                v.Title,
                Performers = v.VideoPerformers.Select(p => p.Performer!.Name).ToList(),
                Tags = v.VideoTags.Select(t => t.Tag!.Name).ToList(),
            }).FirstOrDefaultAsync(ct);
        if (video is null) return new(null, [], [], []);
        var segments = await db.Set<Segment>().AsNoTracking().Where(s => s.HostType == SegmentHostType.Video && s.HostId == id)
            .OrderBy(s => s.StartSec).Select(s => new { s.Id, s.StartSec, s.EndSec, s.Title, Tag = s.Tag != null ? s.Tag.Name : null }).ToListAsync(ct);
        var extra = await db.Set<TagApplication>().AsNoTracking()
            .Where(a => a.HostType == AffinityHostType.Video && a.HostId == id && a.ContextType == "segment" && a.ContextId != null)
            .Select(a => new { a.ContextId, Name = a.Tag!.Name }).ToListAsync(ct);
        var byPart = extra.GroupBy(x => x.ContextId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
        var parts = segments.Select(s => new Part(s.StartSec, s.EndSec,
            new[] { s.Tag }.Concat(byPart.TryGetValue(s.Id, out var more) ? more : []).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            s.Title)).ToList();
        return new(video.Title, video.Performers.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(), video.Tags.ToArray(), parts);
    }
}
