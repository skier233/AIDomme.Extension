namespace AIDome2025.Services;

/// <summary>
/// Centralized domme text pools — commanding, varied, mean-when-appropriate.
/// Every pool has enough entries that sessions feel different each time.
/// </summary>
public static class DommeText
{
    // ──────────────────── Rest / Hands-off ────────────────────

    public static readonly string[] RestNice =
    [
        "Hands off. Don't even think about touching until I tell you.",
        "Stop. Breathe. You don't move until I say so.",
        "Off. Now. Enjoy the break while it lasts.",
        "No touching. Let the ache build.",
        "Hands at your sides. You wait for me.",
    ];

    public static readonly string[] RestMean =
    [
        "Hands off. NOW. Don't you dare twitch.",
        "Stop touching yourself. You think you earned a break? Wrong. I'm giving you one because I FEEL like it.",
        "Off. Every second you rest is a second I'm planning something worse.",
        "Don't move. Don't breathe hard. Just sit there and think about how desperate you are.",
        "Hands off. The next thing I make you do is going to be worse. Enjoy the wait.",
        "No touching. Count your heartbeats. Each one is a reminder that I own your pleasure.",
    ];

    public static readonly string[] RestPunishment =
    [
        "Hands off. This isn't mercy. This is me making the next part hit harder.",
        "Stop. You don't deserve rest but I need you functional for what's coming.",
        "Off. Think about what you did to end up here.",
        "Don't touch. I want the ache to grow before I make it worse.",
    ];

    // ──────────────────── Edge taunts ────────────────────

    public static readonly string[] EdgeTaunts =
    [
        "Don't you dare cum.",
        "Hold it. Right there.",
        "Right on the edge. Stay.",
        "Almost... but not yet. Not until I say.",
        "Feel that? That's where you live now.",
        "So close. And so far from getting what you want.",
        "Edge. Don't go over. You know what happens if you do.",
        "Hold. Let it burn.",
        "That's it. Right on the line. Don't cross it.",
        "Good. Now suffer there for me.",
    ];

    public static readonly string[] EdgeTauntsPunishment =
    [
        "Edge again. You don't get a break.",
        "You're not getting off that easy.",
        "I didn't say stop. Edge.",
        "One more. And you'd better not go over.",
        "Again. And hold it longer this time.",
        "Edge. Every single one is punishment.",
        "Keep edging. This is what you deserve.",
        "Another one. Don't you dare go over.",
    ];

    // ──────────────────── Stroke / tease instructions ────────────────────

    public static readonly string[] StrokeOpeners =
    [
        "Now.",
        "Next.",
        "Keep going.",
        "Don't stop.",
        "Change it up.",
    ];

    public static readonly string[] TeaseLines =
    [
        "Nice and slow. I want you desperate.",
        "Take it slow. Build that ache.",
        "Gentle. I'm not done teasing you yet.",
        "Slow. You don't get to rush this.",
        "Easy. Let it build. Slowly.",
        "Just barely touching. Feel how much you want more.",
    ];

    public static readonly string[] IntenseLines =
    [
        "Don't stop. Don't slow down.",
        "Faster. I didn't say you could take it easy.",
        "Speed up. No mercy.",
        "Full speed. GO.",
        "Don't you dare slow down.",
        "Harder. You can take more than that.",
    ];

    public static readonly string[] TormentLines =
    [
        "This is going to hurt. Good.",
        "No stopping. No matter how bad it gets.",
        "Suffer. That's what this is for.",
        "You think this is bad? I can go worse.",
        "Endure it. Every second.",
        "No escape. Just take it.",
    ];

    // ──────────────────── Retaliation (context-aware) ────────────────────

    public static readonly string[] RetaliationCumDenied =
    [
        "You thought you'd get to cum? That's cute.",
        "Oh, you thought I'd go easy on you? Wrong.",
        "You asked to cum. The answer is no. And now it gets worse.",
        "That little stunt? It's going to cost you.",
        "You dared to beg? Now watch what happens.",
    ];

    public static readonly string[] RetaliationStopDenied =
    [
        "You wanted to stop? Fine. But now you pay.",
        "You think I care that it's too much? Keep going.",
        "Begging to stop only makes me want to push harder.",
        "Stop? We stop when I say we stop.",
        "You asked to stop. That earned you extra.",
    ];

    public static readonly string[] RetaliationDisobedience =
    [
        "You disobeyed. You know what happens now.",
        "That's a mistake you're going to regret.",
        "You want to disobey? Fine. Now I get to punish you.",
        "Bad move. I don't tolerate disobedience.",
        "You just earned yourself something extra. And not the fun kind.",
        "Disobedient. I'll fix that attitude.",
    ];

    public static readonly string[] RetaliationUnauthorizedOrgasm =
    [
        "You came without permission? You're going to pay for that.",
        "Did I say you could cum? No. Now you suffer.",
        "Unauthorized orgasm. This session just got a lot worse for you.",
        "You couldn't help yourself? Pathetic. Time for consequences.",
        "You lost control. That's going to cost you dearly.",
    ];

    public static readonly string[] RetaliationRuinedOrgasm =
    [
        "You ruined it? Good. That's what you deserve.",
        "A ruined orgasm. How does that feel? Not great, right?",
        "You ruined it. And now I'm going to make the rest even worse.",
        "Ruined. You don't even get to enjoy it. Now keep going.",
        "That was pathetic. And now you're going to work even harder.",
    ];

    // ──────────────────── Surprise / stop ────────────────────

    public static readonly string[] SurpriseStops =
    [
        "STOP. Hands at your sides. Don't move.",
        "Freeze. Think about how close you were.",
        "Stop everything. Count to ten. Slowly.",
        "Hands off. NOW. Don't even breathe hard.",
        "Stop. I didn't say you could keep going.",
    ];

    // ──────────────────── Punishment session openers ────────────────────

    public static readonly string[] PunishmentOpeners =
    [
        "This is your punishment. Every second of it.",
        "You brought this on yourself.",
        "You think this is bad? We're just getting started.",
        "Punishment time. No mercy.",
        "This is what happens when you disobey.",
        "You're going to wish you'd behaved.",
    ];

    // ──────────────────── Beg responses ────────────────────

    public static readonly string[] BegCumDenied =
    [
        "Denied. Keep going.",
        "No. You don't deserve it.",
        "Not a chance. Keep stroking.",
        "Denied. And that's going to cost you extra.",
        "The answer is no. Now edge again.",
        "You think begging helps? It makes it worse.",
        "No. And don't ask again unless you want it harder.",
        "Denied. How does that feel?",
    ];

    public static readonly string[] BegCumGranted =
    [
        "Permission granted. You may cum.",
        "Fine. You've earned it. Cum. Now.",
        "Go ahead. But make it count.",
        "You may cum. Don't disappoint me.",
    ];

    public static readonly string[] BegStopDenied =
    [
        "No. Keep going.",
        "You don't get to stop. I decide when this ends.",
        "Too bad. You're not done yet.",
        "Stop? Not a chance. Keep going.",
        "I don't care. You'll take what I give you.",
    ];

    public static readonly string[] BegStopGranted =
    [
        "Fine. You get a break. Don't get used to it.",
        "Alright. But the next step is going to be worse.",
        "I'll let you rest. Only because I want the next part to hurt more.",
    ];

    // ──────────────────── Auto-finish (session end, not user-initiated beg) ────────────────────

    public static readonly string[] AutoFinishGranted =
    [
        "Your time is up. Cum now.",
        "Session's over. I'll let you finish. Make it count.",
        "Time. You've earned it tonight. Go ahead and cum.",
        "Done. You can finish now. Quickly.",
        "That's enough. Cum for me. Now.",
    ];

    // ──────────────────── Finish / denial ────────────────────

    public static readonly string[] FinishDenied =
    [
        "Time is up. You don't get to cum. Accept it.",
        "Session over. You're denied. Walk away.",
        "Done. No orgasm for you. That ache stays with you.",
        "Time. Hands off. You go to bed frustrated tonight.",
    ];

    public static readonly string[] FinishBegPrompt =
    [
        "Time is up. Beg to cum, or accept denial.",
        "Session's ending. This is your last chance to beg.",
        "Time. You can beg, or you can walk away frustrated. Your choice.",
    ];

    // ──────────────────── Psychological ────────────────────

    public static readonly string[] FalsePromise =
    [
        "I think you've been good enough. I might let you cum soon...",
        "Keep going just like that... you might get what you want...",
        "Almost there. A few more edges and maybe...",
        "You know what? I think you deserve a reward...",
    ];

    public static readonly string[] FalsePromiseReveal =
    [
        "Just kidding. Denied.",
        "Changed my mind. Keep edging.",
        "Did you actually believe that? Cute.",
        "I said 'might.' And the answer is no.",
        "You should see your face right now. No.",
    ];

    public static readonly string[] LoyaltyTest =
    [
        "You'd give up your orgasm if that's what I wanted. And you know it.",
        "If I told you to stop right now and walk away — you would. Because I own you.",
        "Cumming doesn't matter. Doing what I say matters. Remember that.",
        "You don't get to decide when this ends. I do. Keep stroking.",
        "Good boys don't ask questions. They obey. Now keep going.",
        "Every edge you give me is proof that you belong to me.",
    ];

    public static readonly string[] ArousalChallengeStart =
    [
        "Challenge time. Don't edge within the time limit, and you earn a reward.",
        "Let's see how much control you have. Don't go over.",
        "Arousal challenge. Edge = you lose. Ready?",
    ];

    public static readonly string[] ArousalChallengeWin =
    [
        "Impressive. You actually held it. Points earned.",
        "Good boy. You passed. For now.",
        "You managed. I'll remember that.",
    ];

    public static readonly string[] ArousalChallengeLose =
    [
        "You lost. That's going to cost you.",
        "Couldn't handle it? Pathetic. Punishment incoming.",
        "Failed. I expected better. I expected wrong.",
    ];

    // ──────────────────── Point economy ────────────────────

    public static readonly string[] PointsEarned =
    [
        "Point earned.",
        "+1. Don't spend it all in one place.",
        "Good. That's a point.",
    ];

    public static readonly string[] PointsLost =
    [
        "Point deducted.",
        "-1. You earned that.",
        "Penalty. Point lost.",
    ];

    // ──────────────────── Game announcements ────────────────────

    public static readonly string[] GameIntros =
    [
        "Let's play a game.",
        "Time for a little game. The stakes are real.",
        "Game time. Pay attention.",
        "I have a game for you.",
        "Let's make this interesting.",
        "We're going to play a game. Listen carefully.",
    ];

    // ──────────────────── Confession / cross-session ────────────────────

    public static readonly string[] ConfessionPunishment =
    [
        "At least you were honest. That doesn't mean you're off the hook.",
        "You came without permission. You know what that means.",
        "Confession noted. Punishment assigned. Don't let it happen again.",
        "So you couldn't help yourself. Pathetic. You're going to pay for that.",
        "Confessing earns you a fraction of mercy. The rest is punishment.",
    ];

    public static readonly string[] ConfessionDenied =
    [
        "Good. I'll believe you. This time.",
        "Fine. But if I find out you're lying...",
        "Alright. But I'm watching you.",
        "I'll take your word for it. Don't make me regret it.",
    ];

    public static readonly string[] PunishmentSessionIntro =
    [
        "You're being punished. Remember why you're here.",
        "This session is punishment. Every second of it.",
        "Time to serve your sentence. No mercy.",
    ];

    public static readonly string[] ActivePunishmentReminder =
    [
        "Don't forget — you're still serving a punishment.",
        "Your punishment is still active. This session reflects that.",
        "Remember your punishment. It colors everything we do today.",
    ];

    // ──────────────────── Helpers ────────────────────

    /// <summary>Pick a random string from a pool.</summary>
    public static string Pick(string[] pool) => pool[Random.Shared.Next(pool.Length)];

    /// <summary>Pick rest text based on mean level and punishment mode.</summary>
    public static string PickRest(int mean, bool punishment)
    {
        if (punishment) return Pick(RestPunishment);
        if (mean >= 6) return Pick(RestMean);
        return Pick(RestNice);
    }

    /// <summary>Pick edge taunt based on punishment mode.</summary>
    public static string PickEdgeTaunt(bool punishment)
        => punishment ? Pick(EdgeTauntsPunishment) : Pick(EdgeTaunts);

    /// <summary>Pick retaliation opener based on what triggered it.</summary>
    public static string PickRetaliation(RetaliationReason reason) => reason switch
    {
        RetaliationReason.BegStopDenied => Pick(RetaliationStopDenied),
        RetaliationReason.Disobedience => Pick(RetaliationDisobedience),
        RetaliationReason.UnauthorizedOrgasm => Pick(RetaliationUnauthorizedOrgasm),
        RetaliationReason.RuinedOrgasm => Pick(RetaliationRuinedOrgasm),
        _ => Pick(RetaliationCumDenied),
    };
}
