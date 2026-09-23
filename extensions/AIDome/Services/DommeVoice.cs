using AIDome.Data;
using AIDome.Sessions;

namespace AIDome.Services;

/// <summary>
/// Conversational speech compositor for the AI domme.
/// Produces TTS-ready natural language by embedding instructions into
/// complete sentences rather than assembling disconnected fragments.
/// </summary>
public static class DommeVoice
{
    public sealed class SpeechContext
    {
        public DommeMood Mood { get; init; }
        public double MoodIntensity { get; init; }
        public double Arousal { get; init; }           // 0-100
        public double Progress { get; init; }          // 0.0-1.0
        public int StepNumber { get; init; }
        public int EdgesThisSession { get; init; }
        public int EdgesThisStep { get; init; }
        public bool IsRetaliating { get; init; }
        public RetaliationReason RetaliationReason { get; init; }
        public bool IsPunishment { get; init; }
        public MethodInfo? Method { get; init; }
        public StepCategory? PreviousCategory { get; init; }
        public StepCategory Category { get; init; }
        public int Speed { get; init; }
        public int Mean { get; init; }
        public int DurationOrCount { get; init; }
        public int HoldSeconds { get; init; }
        public bool LubeActive { get; init; }
        public int SessionPoints { get; init; }
        public bool FalsePromiseActive { get; init; }
        public bool IsEdgeCount { get; init; }
    }


    // ────────────────────────────────────────────────────────────
    // Speed / duration / count → natural language
    // ────────────────────────────────────────────────────────────

    private static string SpeedAdverb(int speed) => speed switch
    {
        <= 0 => "at your own pace",
        <= 2 => "nice and slow",
        3 => "slowly",
        4 => "at a steady pace",
        5 => "at a medium pace",
        6 => "a little faster",
        7 => "fast",
        8 => "nice and fast",
        9 => "as fast as you can",
        _ => "full speed"
    };

    private static string SpeedAdj(int speed) => speed switch
    {
        <= 0 => "self-selected",
        <= 2 => "slow",
        3 or 4 => "steady",
        5 or 6 => "moderate",
        7 or 8 => "fast",
        _ => "maximum"
    };

    private static string DurationPhrase(int seconds) => seconds switch
    {
        <= 5 => "a few seconds",
        <= 15 => $"about {seconds} seconds",
        <= 30 => $"{seconds} seconds",
        <= 45 => "about half a minute",
        <= 75 => $"about {seconds} seconds",
        <= 100 => "about a minute and a half",
        <= 150 => $"about {seconds / 60} minutes",
        _ => $"the next {seconds / 60} minutes or so"
    };

    private static string EdgeCountPhrase(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => $"{count} times"
    };

    private static string EdgeCountNoun(int count) => count switch
    {
        1 => "one edge",
        2 => "two edges",
        _ => $"{count} edges"
    };

    // ────────────────────────────────────────────────────────────
    // Primary composition methods
    // ────────────────────────────────────────────────────────────

    public static string ComposeTease(SpeechContext ctx)
    {
        string trans = PickTransition(ctx);
        string desc = Desc(ctx.Method);
        string spd = SpeedAdverb(ctx.Speed);

        if (ctx.IsEdgeCount)
        {
            string[] t = MoodVariants(ctx.Mood,
                neutral: [
                    $"{trans} {desc}. Go {spd} and edge for me {EdgeCountPhrase(ctx.DurationOrCount)}.",
                    $"{trans} {desc}, {spd}. Give me {EdgeCountNoun(ctx.DurationOrCount)}.",
                ],
                cruel: [
                    $"{trans} {desc}. {EdgeCountNoun(ctx.DurationOrCount)}, {spd}. Don't you dare go over.",
                    $"{trans} {desc}, {spd}. I want {EdgeCountNoun(ctx.DurationOrCount)} and you better not slip.",
                ],
                teasing: [
                    $"{trans} Let's start easy. {desc}, {spd}. Give me {EdgeCountNoun(ctx.DurationOrCount)}.",
                    $"{trans} {desc}, nice and gentle. Edge {EdgeCountPhrase(ctx.DurationOrCount)} for me.",
                ],
                playful: [
                    $"{trans} {desc}, {spd}. Let's see {EdgeCountNoun(ctx.DurationOrCount)}!",
                ]);
            return Pick(t);
        }

        string dur = DurationPhrase(ctx.DurationOrCount);
        string[] templates = MoodVariants(ctx.Mood,
            neutral: [
                $"{trans} {desc}. Go {spd} for {dur}.",
                $"{trans} {desc}, {spd}. Keep that going for {dur}.",
                $"{trans} {desc}. {SpeedAdj(ctx.Speed)} pace, {dur}.",
            ],
            cruel: [
                $"{trans} {desc}, {spd}. You have {dur}. Don't stop.",
                $"{trans} {desc}. {spd}. {dur}. No complaints.",
            ],
            teasing: [
                $"{trans} {desc}, {spd}, for {dur}. Take your time with it.",
                $"{trans} {desc}, {spd}. Just enjoy it for {dur}.",
            ],
            playful: [
                $"{trans} {desc}! Go {spd} for {dur}.",
                $"{trans} {desc}, {spd}, for {dur}.",
            ]);
        return Pick(templates);
    }

    public static string ComposeStroke(SpeechContext ctx)
    {
        string trans = PickTransition(ctx);
        string desc = Desc(ctx.Method);
        string spd = SpeedAdverb(ctx.Speed);

        if (ctx.IsEdgeCount)
        {
            string edgeNote = ctx.Method?.Closeness >= 4 ? " Press edge when you get there." : "";
            string[] t = MoodVariants(ctx.Mood,
                neutral: [
                    $"{trans} {desc}, {spd}. I want {EdgeCountNoun(ctx.DurationOrCount)}.{edgeNote}",
                    $"{trans} {desc}. Go {spd} and give me {EdgeCountNoun(ctx.DurationOrCount)}.{edgeNote}",
                ],
                cruel: [
                    $"{trans} {desc}. {spd}. {EdgeCountNoun(ctx.DurationOrCount)} — no excuses.{edgeNote}",
                    $"{trans} {desc}, {spd}. I want {EdgeCountNoun(ctx.DurationOrCount)}. Don't disappoint me.{edgeNote}",
                ],
                teasing: [
                    $"{trans} {desc}, {spd}. Let's see {EdgeCountNoun(ctx.DurationOrCount)}.{edgeNote}",
                    $"{trans} {desc}. Go {spd}. I want to see you edge {EdgeCountPhrase(ctx.DurationOrCount)}.{edgeNote}",
                ],
                edgy: [
                    $"{trans} {desc}, {spd}. {EdgeCountNoun(ctx.DurationOrCount)}. Go.{edgeNote}",
                ],
                playful: [
                    $"{trans} {desc}, go {spd}. Give me {EdgeCountNoun(ctx.DurationOrCount)}!{edgeNote}",
                ],
                relentless: [
                    $"{trans} {desc}. {spd}. {EdgeCountNoun(ctx.DurationOrCount)}. Now.{edgeNote}",
                ]);
            return AddArousalComment(ctx, Pick(t));
        }

        string dur = DurationPhrase(ctx.DurationOrCount);
        string edgeHint = ctx.Method?.Closeness >= 4 ? " Press edge if you get close." : "";
        string[] templates = MoodVariants(ctx.Mood,
            neutral: [
                $"{trans} {desc}, {spd}. Keep going for {dur}.{edgeHint}",
                $"{trans} {desc} for the next {dur}. Go {spd}.{edgeHint}",
                $"{trans} {desc}, {spd}. You have {dur}.{edgeHint}",
            ],
            cruel: [
                $"{trans} {desc}. Go {spd}. You have {dur} and you're not stopping.{edgeHint}",
                $"{trans} {desc}. {spd}, {dur}. No slowing down.{edgeHint}",
            ],
            teasing: [
                $"{trans} {desc}. {spd}. Enjoy every second of the next {dur}.{edgeHint}",
                $"{trans} {desc}, {spd}. {dur}. Make it feel good.{edgeHint}",
            ],
            edgy: [
                $"{trans} {desc}. {spd}. {dur}. Go.{edgeHint}",
                $"{trans} {desc}, {spd}, {dur}. Don't stop.{edgeHint}",
            ],
            playful: [
                $"{trans} {desc}! Go {spd} for {dur}.{edgeHint}",
                $"{trans} {desc}, {spd} for {dur}. Let's see how you do.{edgeHint}",
            ],
            relentless: [
                $"{trans} {desc}. {spd}. {dur}. Keep going.{edgeHint}",
                $"{trans} {desc}. {spd}. {dur}. Don't slow down.{edgeHint}",
            ]);
        return AddArousalComment(ctx, Pick(templates));
    }

    public static string ComposeEdge(SpeechContext ctx)
    {
        string trans = PickTransition(ctx);
        string desc = Desc(ctx.Method);
        string spd = SpeedAdverb(ctx.Speed);

        if (!ctx.IsEdgeCount)
        {
            string dur = DurationPhrase(ctx.DurationOrCount);
            string[] t = MoodVariants(ctx.Mood,
                neutral: [
                    $"{trans} {desc}, {spd}, for {dur}. Push yourself.",
                    $"{trans} {desc}. Go {spd} for {dur}. Get as close as you can.",
                ],
                cruel: [
                    $"{trans} {desc}. {spd}. {dur}. I want you suffering every second.",
                    $"{trans} {desc}, {spd}. {dur}. Don't even think about slowing down.",
                ],
                teasing: [
                    $"{trans} {desc}, {spd}. See how close you can get in {dur}.",
                ]);
            return Pick(t);
        }

        string holdText = "";
        if (ctx.HoldSeconds > 0 && ctx.DurationOrCount > 1)
            holdText = $" When you hit the last one, hold it for {DurationPhrase(ctx.HoldSeconds)}.";
        else if (ctx.HoldSeconds > 0)
            holdText = $" When you get there, hold it for {DurationPhrase(ctx.HoldSeconds)}.";

        string[] templates = MoodVariants(ctx.Mood,
            neutral: [
                $"{trans} {desc}, {spd}. I want you to edge {EdgeCountPhrase(ctx.DurationOrCount)}.{holdText}",
                $"{trans} {desc}. Go {spd}. Give me {EdgeCountNoun(ctx.DurationOrCount)}.{holdText}",
            ],
            cruel: [
                $"{trans} {desc}. {spd}. Edge {EdgeCountPhrase(ctx.DurationOrCount)} for me. No going over.{holdText}",
                $"{trans} {desc}. I want {EdgeCountNoun(ctx.DurationOrCount)}. Get right to the edge, {spd}. Suffer.{holdText}",
            ],
            teasing: [
                $"{trans} {desc}, {spd}. Edge {EdgeCountPhrase(ctx.DurationOrCount)}. I want to see you desperate.{holdText}",
                $"{trans} {desc}. Go {spd}. Get right to the edge, {EdgeCountPhrase(ctx.DurationOrCount)}. Feel that ache.{holdText}",
            ],
            edgy: [
                $"{trans} {desc}. {spd}. Edge {EdgeCountPhrase(ctx.DurationOrCount)}. Now.{holdText}",
                $"{trans} {desc}. {spd}. {EdgeCountNoun(ctx.DurationOrCount)}. Go.{holdText}",
            ],
            playful: [
                $"{trans} Let's push it! {desc}, {spd}. Edge {EdgeCountPhrase(ctx.DurationOrCount)} for me!{holdText}",
            ],
            relentless: [
                $"{trans} {desc}. {spd}. {EdgeCountNoun(ctx.DurationOrCount)}. Don't stop until you're right on the line.{holdText}",
            ]);
        return Pick(templates);
    }

    public static string ComposeRest(SpeechContext ctx)
    {
        string dur = DurationPhrase(ctx.DurationOrCount);

        string[] rest;
        if (ctx.IsPunishment)
            rest = [$"{DommeText.Pick(DommeText.RestPunishment)} You have {dur}."];
        else if (ctx.IsRetaliating)
            rest = [$"{DommeText.Pick(DommeText.RestMean)} {dur}."];
        else
        {
            rest = MoodVariants(ctx.Mood,
                neutral: [
                    $"Take a break. Hands off for {dur}.",
                    $"Rest for {dur}. Catch your breath.",
                    $"Hands off. You have {dur}.",
                ],
                cruel: [
                    $"Fine. {dur}. Don't get used to it.",
                    $"You get {dur} off. That's all the mercy you're getting.",
                    $"Stop. {dur}. Then it gets worse.",
                ],
                teasing: [
                    $"Hands off. Take {dur} to think about how badly you want to keep going.",
                    $"Break time. {dur}. Let that desire build.",
                    $"Rest for {dur}. But don't let that arousal fade too much.",
                ],
                edgy: [
                    $"Stop. {dur}. Breathe.",
                    $"Off. {dur}. We go again after.",
                ],
                playful: [
                    $"Break time! {dur}. Catch your breath.",
                    $"Okay, take {dur}. You earned it. Sort of.",
                ],
                relentless: [
                    $"Quick break. {dur}. Then we go harder.",
                    $"Fine. {dur}. Don't get comfortable.",
                ]);
        }

        string progressNote = ProgressComment(ctx);
        return Combine(Pick(rest), progressNote);
    }

    public static string ComposeTorment(SpeechContext ctx)
    {
        string desc = Desc(ctx.Method);
        string spd = SpeedAdverb(ctx.Speed);
        string dur = DurationPhrase(ctx.DurationOrCount);

        string opener;
        if (ctx.IsRetaliating)
            opener = DommeText.PickRetaliation(ctx.RetaliationReason);
        else if (ctx.IsPunishment)
            opener = DommeText.Pick(DommeText.PunishmentOpeners);
        else
        {
            opener = PickFrom(ctx.Mood,
                neutral: DommeText.TormentLines,
                cruel: [
                    "This is going to hurt and I don't care.",
                    "Suffer. That's what you're here for.",
                    "No mercy on this one.",
                ],
                teasing: [
                    "This might be a little intense for you.",
                    "Let's see how much you can actually handle.",
                ],
                edgy: ["Take it.", "No escape.", "Endure."],
                playful: ["Ooh, this one's going to be fun. For me.", "Hope you're ready!"],
                relentless: ["More. Don't stop.", "Keep going. Harder."]);
        }

        string edgeNote = ctx.Method?.Closeness >= 4 ? " Press edge if you hit the edge." : "";
        string[] templates =
        [
            $"{opener} {desc}, {spd}, for {dur}. No stopping.{edgeNote}",
            $"{opener} {desc}. {spd}. Keep it going for {dur}. Don't you dare stop.{edgeNote}",
            $"{opener} {desc}. Go {spd} for {dur}. Take every second of it.{edgeNote}",
        ];
        return Pick(templates);
    }

    public static string ComposeSurpriseStop(SpeechContext ctx, int duration)
    {
        string dur = DurationPhrase(duration);
        string[] stops = MoodVariants(ctx.Mood,
            neutral: [
                $"Stop. Hands off for {dur}.",
                $"Freeze. Don't touch anything for {dur}.",
            ],
            cruel: [
                $"STOP. Hands off. Don't move for {dur}. Think about how close you were.",
                $"Off. Now. {dur}. Suffer.",
            ],
            teasing: [
                $"Stop. Just... stop. Hands off for {dur}. Feel that frustration.",
                $"Pause. Let the ache build for {dur}.",
            ],
            edgy: [$"Stop. {dur}.", $"Off. {dur}. Now."],
            playful: [$"STOP! Gotcha. Hands off for {dur}.", $"Surprise! Pause for {dur}."],
            relentless: [$"STOP. {dur}. Brief pause, then we go again.", $"Off. {dur}."]);
        return Pick(stops);
    }

    public static string ComposeSurpriseBurst(SpeechContext ctx, MethodInfo method, int duration)
    {
        string desc = Desc(method);
        string dur = DurationPhrase(duration);
        string[] lines =
        [
            $"Full speed, now. {desc} for {dur}. Go. Do NOT cum.",
            $"Speed up. {desc}, as fast as you can for {dur}. Don't you dare stop.",
            $"Fast as you can. {desc} for {dur}. No slowing down.",
        ];
        return Pick(lines);
    }

    public static string ComposeSurpriseSwap(SpeechContext ctx, MethodInfo method, int speed, int duration)
    {
        string desc = Desc(method);
        string spd = SpeedAdverb(speed);
        string dur = DurationPhrase(duration);
        string[] lines =
        [
            $"Change of plans. {desc}. {spd}, for {dur}.",
            $"New instruction. {desc}, {spd}. You have {dur}. No complaining.",
            $"Switch it up. {desc}, {spd}, {dur}. Deal with it.",
        ];
        return Pick(lines);
    }

    public static string ComposeEdgeHold(SpeechContext ctx, int holdSeconds)
    {
        string dur = DurationPhrase(holdSeconds);
        string[] holds = MoodVariants(ctx.Mood,
            neutral: [
                $"Right there. Hold it for {dur}. Don't go over.",
                $"Stay on that edge. {dur}. Breathe through it.",
            ],
            cruel: [
                $"Hold it right there. {dur}. I want you suffering.",
                $"Stay. Don't you dare cum. {dur}. Feel that agony.",
            ],
            teasing: [
                $"Hold it... right there. {dur}. Feel how badly you want to go over.",
                $"Almost... but not yet. Stay right there for {dur}.",
            ],
            edgy: [$"Hold. {dur}. Don't move.", $"Right there. {dur}. Stay."],
            playful: [$"Whoa, hold it! Stay right there for {dur}!", $"Nice edge! Hold it for {dur}."],
            relentless: [$"Don't move. Hold that edge for {dur}.", $"Stay right there. {dur}. You're not done."]);
        return Pick(holds);
    }

    public static string ComposeEdgeCountHold(SpeechContext ctx, int holdSeconds)
    {
        return ComposeEdgeHold(ctx, holdSeconds);
    }

    public static string ComposeAutoFinishGranted(SpeechContext ctx, MethodInfo method, int duration)
    {
        string desc = Desc(method);
        string dur = DurationPhrase(duration);
        string[] granted = MoodVariants(ctx.Mood,
            neutral: [
                $"Your time is up and you've earned it. {desc}. You have {dur} to finish.",
                $"Session's over. I'll let you cum. {desc} — you have {dur}. Make it count.",
            ],
            cruel: [
                $"Fine. You can finish. {desc}, {dur}. Hurry up.",
                $"You get to cum. Don't thank me. {desc}, {dur}.",
            ],
            teasing: [
                $"I've been teasing you long enough. Go ahead. {desc}, {dur}. Enjoy it.",
                $"You've suffered enough — for now. {desc}, {dur}.",
            ],
            edgy: [$"Cum. Now. {desc}. You have {dur}. Don't waste my time."],
            playful: [
                $"Surprise! You actually get to finish. {desc}, {dur}!",
                $"Lucky you. {desc}, {dur}. Don't mess it up.",
            ],
            relentless: [$"Finish. {desc}. {dur}. GO."]);
        return Pick(granted);
    }

    public static string ComposeAutoFinishDenied(SpeechContext ctx)
    {
        string[] denied = MoodVariants(ctx.Mood,
            neutral: DommeText.FinishDenied,
            cruel: [
                "Denied. You knew it was coming. Walk away.",
                "No. You don't deserve it tonight.",
                "Did you really think I'd let you cum? Denied.",
            ],
            teasing: [
                "Almost... but no. Denied. That ache stays with you tonight.",
                "You were so hopeful. That's cute. Denied.",
            ],
            edgy: [
                "No. Denied. Session over. Don't touch yourself.",
                "Denied. You're done.",
            ],
            playful: [
                "Nope! Better luck next time. Denied.",
                "Denied! But hey, you had fun, right?",
            ],
            relentless: [
                "No. You don't get to finish. Get out.",
                "Denied. All that work and nothing to show for it.",
            ]);
        return Pick(denied);
    }

    public static string ComposeFalsePromiseReveal(SpeechContext ctx, MethodInfo method, int extraDuration)
    {
        string reveal = DommeText.Pick(DommeText.FalsePromiseReveal);
        string desc = Desc(method);
        string dur = DurationPhrase(extraDuration);
        return $"{reveal} Now — {desc} for {dur}. No stopping.";
    }

    public static string ComposePsychLoyalty(SpeechContext ctx, MethodInfo method, int speed, int duration)
    {
        string desc = Desc(method);
        string spd = SpeedAdverb(speed);
        string dur = DurationPhrase(duration);

        string loyalty = PickFrom(ctx.Mood,
            neutral: DommeText.LoyaltyTest,
            cruel: [
                "You belong to me. Every edge proves it.",
                "You don't decide anything here. I do.",
            ],
            teasing: [
                "You'd give it all up if I asked, wouldn't you?",
                "I own your pleasure and you know it.",
            ],
            edgy: ["Focus on obeying. Nothing else matters."],
            playful: ["You'd do anything I say, right? Right."],
            relentless: ["No thinking. Just obeying."]);

        string[] templates =
        [
            $"{loyalty} {desc}, {spd}, for {dur}.",
            $"{loyalty} Keep going. {desc}. {spd}. {dur}.",
        ];
        return Pick(templates);
    }

    public static string ComposePsychDenialTaunt(SpeechContext ctx, MethodInfo method, int speed, int duration)
    {
        string desc = Desc(method);
        string spd = SpeedAdverb(speed);
        string dur = DurationPhrase(duration);

        string taunt;
        if (ctx.Mean >= 7)
        {
            string[] harsh =
            [
                "You're not cumming tonight. You know that, right? Every edge just makes it worse.",
                "No orgasm for you. Not even close.",
                "Denied. And every stroke makes the ache deeper.",
            ];
            taunt = Pick(harsh);
        }
        else
        {
            string[] mild =
            [
                "I wonder if you've earned anything tonight. Probably not.",
                "Think you deserve to cum? We'll see.",
                "I haven't decided your fate yet.",
            ];
            taunt = Pick(mild);
        }

        return $"{taunt} {desc}, {spd}, for {dur}.";
    }

    public static string ComposeResume(SpeechContext ctx, MethodInfo method, int speed, int remaining)
    {
        string desc = Desc(method);
        string spd = SpeedAdverb(speed);
        string dur = DurationPhrase(remaining);
        string[] lines =
        [
            $"Keep going. {desc}, {spd}. You still have {dur} left.",
            $"Back to it. {desc}, {spd}. {dur} remaining.",
            $"Don't get comfortable. {desc}, {spd}. {dur} to go.",
        ];
        return Pick(lines);
    }

    public static string ComposeCooldown(SpeechContext ctx, int seconds)
    {
        string dur = DurationPhrase(seconds);
        string edgeAck = ctx.EdgesThisSession > 0
            ? $" That's {ctx.EdgesThisSession} edges so far."
            : "";

        string[] stops = MoodVariants(ctx.Mood,
            neutral: [$"Good edge. Hands off for {dur}.{edgeAck}", $"Stop. Catch your breath for {dur}.{edgeAck}"],
            cruel: [$"Stop. {dur}. You're not done yet.{edgeAck}", $"Off. {dur}. Then we go again.{edgeAck}"],
            teasing: [$"Nice edge. Rest for {dur}. Feel that ache.{edgeAck}"],
            edgy: [$"Off. {dur}.{edgeAck}"],
            playful: [$"Good one! Take {dur} to recover.{edgeAck}"],
            relentless: [$"Stop. Brief pause. {dur}.{edgeAck}"]);
        return Pick(stops);
    }

    public static string ComposeSessionEnd(SpeechContext ctx, bool wasOrgasmGranted)
    {
        if (wasOrgasmGranted)
        {
            return PickFrom(ctx.Mood,
                neutral: ["Session complete. Good boy.", "That's the end. You did well.", "Done. Hope you enjoyed it."],
                cruel: ["Session over. Don't expect that every time.", "Done. You got lucky tonight.", "That's it. Don't get used to finishing."],
                teasing: ["Session's over. How did that feel?", "Done. Bet you enjoyed that.", "All done. Sweet dreams."],
                edgy: ["Done. Session over.", "Finished.", "Session complete."],
                playful: ["That was fun! Session over.", "All done! Good job!", "Session complete!"],
                relentless: ["Done. Session over. Go.", "Finished. Out.", "Session complete."]);
        }

        return PickFrom(ctx.Mood,
            neutral: ["Session has ended. Until next time.", "That's the end.", "Session over."],
            cruel: ["Get out. Session's done.", "You're dismissed. Don't touch yourself.", "Done. Leave."],
            teasing: ["Session's over. Miss me already?", "Until next time...", "Done for now. Think about me."],
            edgy: ["Done.", "Session over.", "That's it."],
            playful: ["Session done! See you next time.", "All done for now!", "Bye for now!"],
            relentless: ["Out. We're done.", "Session over. Leave.", "Done."]);
    }

    // ────────────────────────────────────────────────────────────
    // Transition pickers — what to say based on what just happened
    // ────────────────────────────────────────────────────────────

    private static string PickTransition(SpeechContext ctx)
    {
        if (ctx.IsRetaliating)
            return DommeText.PickRetaliation(ctx.RetaliationReason);

        if (ctx.FalsePromiseActive)
            return DommeText.Pick(DommeText.FalsePromise);

        if (ctx.PreviousCategory is null)
        {
            return PickFrom(ctx.Mood,
                neutral: ["Let's start.", "Here we go.", "Ready?", "Time to begin."],
                cruel: ["Let's begin.", "Time to work.", "We start now.", "No warm-up."],
                teasing: ["Let's have some fun.", "Ready to play?", "This should be fun.", "Let's get started."],
                edgy: ["Start.", "Go.", "Begin."],
                playful: ["Let's go!", "Here we go!", "Ready? Let's start!", "Let's do this!"],
                relentless: ["Go.", "Start. Now.", "We begin.", "No warm-up."]);
        }

        return ctx.PreviousCategory.Value switch
        {
            StepCategory.Rest => PickFrom(ctx.Mood,
                neutral: ["Break's over.", "Back to work.", "Enough rest.", "Let's continue."],
                cruel: ["That's enough mercy.", "Back to suffering.", "Rest is over, now it gets worse.", "Done resting."],
                teasing: ["Miss me?", "Ready for more?", "Hope you enjoyed the break.", "Feeling refreshed? Good. You'll need it."],
                edgy: ["Back to it.", "Let's go.", "No more waiting.", "Go."],
                playful: ["Okay break's over!", "Back to the fun.", "Let's continue!", "Refreshed? Good!"],
                relentless: ["Break's done. Go.", "Back to it.", "No more rest."]),

            StepCategory.Edge => PickFrom(ctx.Mood,
                neutral: ["Good.", "Now.", "Okay.", "Moving on.", "Next."],
                cruel: ["Good edge. Now more suffering.", "Still shaking? Too bad.", "Bet that hurt.", "That was just the start."],
                teasing: ["Mmm, that edge looked intense.", "How does that feel?", "Nice and desperate now.", "Bet you want to cum so badly right now."],
                edgy: ["More.", "Keep it up.", "Next.", "Go."],
                playful: ["Nice one!", "Good edge!", "Let's keep going.", "Not bad!"],
                relentless: ["Don't stop.", "Next.", "Keep going.", "No breaks."]),

            StepCategory.Torment => PickFrom(ctx.Mood,
                neutral: ["Okay.", "Moving on.", "Something different now."],
                cruel: ["Not done yet.", "You survived that. Barely.", "More."],
                teasing: ["Wasn't that fun?", "Now something different.", "Let's try this."],
                edgy: ["Next.", "Keep going."],
                playful: ["Okay, something different!", "Change of pace."],
                relentless: ["No slowing down.", "More.", "Next."]),

            StepCategory.Tease => PickFrom(ctx.Mood,
                neutral: ["Now.", "Keep going.", "Next.", "New instruction."],
                cruel: ["Enough teasing. Time to work.", "That was the easy part.", "Now it gets real."],
                teasing: ["Want more?", "Getting desperate yet?", "Let me tease you a bit more.", "How about something different?"],
                edgy: ["More.", "Keep building.", "Next."],
                playful: ["Okay, new instruction!", "Switching it up.", "Something different now."],
                relentless: ["Faster now.", "Keep going.", "Don't slow down.", "No breaks."]),

            StepCategory.Surprise => PickFrom(ctx.Mood,
                neutral: ["Now.", "Moving on.", "Okay.", "Continuing."],
                cruel: ["Surprised? Good.", "You didn't expect that, did you?", "More where that came from."],
                teasing: ["Surprised you, didn't I?", "Different pace now.", "Now then."],
                edgy: ["Focus.", "Back to it.", "Moving on."],
                playful: ["That was fun!", "Okay, back to it.", "Moving on!"],
                relentless: ["No time to recover.", "More.", "Keep going."]),

            StepCategory.Game => PickFrom(ctx.Mood,
                neutral: ["Game over. Back to work.", "Now.", "Moving on."],
                cruel: ["Done playing. Now you work.", "Games are over.", "Fun's over."],
                teasing: ["Fun game. Now back to this.", "Now the real fun.", "Back to the main event."],
                edgy: ["Focus.", "Back to stroking.", "Focus up."],
                playful: ["That was fun! Now...", "Game over, back to it.", "Okay, playtime's over."],
                relentless: ["Done. Now work.", "No more games.", "Move."]),

            _ => PickFrom(ctx.Mood,
                neutral: ["Now.", "Next.", "Okay.", "New instruction.", "Moving on.", "Continuing."],
                cruel: ["Keep going.", "More.", "Don't slow down.", "No mercy."],
                teasing: ["Something different now.", "Let's switch it up.", "I have something for you.", "New instruction."],
                edgy: ["Go.", "Now.", "Next."],
                playful: ["Next up!", "Here's what's next.", "Something new!", "Switching it up."],
                relentless: ["Go.", "More.", "Now.", "Keep moving."]),
        };
    }

    // ────────────────────────────────────────────────────────────
    // Arousal / progress commentary (injected sparingly)
    // ────────────────────────────────────────────────────────────

    private static string AddArousalComment(SpeechContext ctx, string text)
    {
        if (ctx.Arousal < 50 && ctx.EdgesThisSession < 3) return text;
        if (Random.Shared.Next(100) >= 30) return text;

        string comment;
        if (ctx.Arousal >= 85)
        {
            string[] high = [
                "You're barely hanging on, I can tell.",
                "I can tell you're close.",
                "Careful... don't you dare cum.",
                "Almost at your limit.",
            ];
            comment = Pick(high);
        }
        else if (ctx.Arousal >= 65)
        {
            string[] mid = [
                "Getting worked up, aren't you?",
                "I can feel you getting desperate.",
                $"That's {ctx.EdgesThisSession} edges now and counting.",
            ];
            comment = Pick(mid);
        }
        else if (ctx.EdgesThisSession >= 5)
        {
            string[] edgeCount = [
                $"That's {ctx.EdgesThisSession} edges now.",
                $"{ctx.EdgesThisSession} edges. How many more can you take?",
            ];
            comment = Pick(edgeCount);
        }
        else return text;

        return $"{text} {comment}";
    }

    private static string ProgressComment(SpeechContext ctx)
    {
        if (Random.Shared.Next(100) >= 30) return "";

        if (ctx.Progress < 0.2)
            return Pick(["We're just getting started.", "Long way to go.", "This is only the beginning."]);
        if (ctx.Progress > 0.8)
            return Pick(["Almost done.", "Not much longer now.", "The end is near. Maybe."]);
        if (ctx.Progress > 0.5)
            return Pick(["Halfway there.", "Past the midpoint.", "You're getting through it."]);

        return "";
    }

    // ────────────────────────────────────────────────────────────
    // Utility
    // ────────────────────────────────────────────────────────────

    private static string Desc(MethodInfo? m)
    {
        if (m is null) return "Stroke";
        // The user owns methods: same lines, method-agnostic wording.
        if (m.IsUserChoice) return "Stroke however you like";
        string d = m.Description.TrimEnd('.', ' ');
        if (!string.IsNullOrWhiteSpace(d)) return d;
        return string.IsNullOrWhiteSpace(m.Name) ? "Stroke" : m.Name;
    }

    private static string Pick(string[] arr) => arr[Random.Shared.Next(arr.Length)];

    private static string Combine(params string[] parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();

    private static string PickFrom(DommeMood mood,
        string[] neutral, string[]? cruel = null, string[]? teasing = null,
        string[]? edgy = null, string[]? playful = null, string[]? relentless = null)
    {
        var pool = mood switch
        {
            DommeMood.Cruel => cruel ?? neutral,
            DommeMood.Teasing => teasing ?? neutral,
            DommeMood.Edgy => edgy ?? neutral,
            DommeMood.Playful => playful ?? neutral,
            DommeMood.Relentless => relentless ?? neutral,
            _ => neutral
        };
        return pool[Random.Shared.Next(pool.Length)];
    }

    private static string[] MoodVariants(DommeMood mood,
        string[] neutral, string[]? cruel = null, string[]? teasing = null,
        string[]? edgy = null, string[]? playful = null, string[]? relentless = null)
    {
        return mood switch
        {
            DommeMood.Cruel => cruel ?? neutral,
            DommeMood.Teasing => teasing ?? neutral,
            DommeMood.Edgy => edgy ?? neutral,
            DommeMood.Playful => playful ?? neutral,
            DommeMood.Relentless => relentless ?? neutral,
            _ => neutral
        };
    }
}
