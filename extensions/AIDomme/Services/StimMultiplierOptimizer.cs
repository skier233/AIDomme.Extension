using AIDomme.Data;

namespace AIDomme.Services;

public static class StimMultiplierOptimizer
{
    private const double MinTimePointsPerSecond = 1d / 120d;
    private const double MinEdgePointsPerEdge = 1d;
    private const double ProbabilityEpsilon = 1e-9;

    public sealed record TrialResult(string Name, double ValidationLogLoss, double ValidationAccuracy);

    public sealed record OptimizationResult(
        string WinningAlgorithm,
        double ValidationLogLoss,
        double ValidationAccuracy,
        IReadOnlyList<TrialResult> Trials,
        IReadOnlyDictionary<int, (double TimePointsPerSecond, double EdgePointsPerEdge)> Multipliers);

    private sealed record ParsedCandidate(int MethodId, bool IsEdge, int Amount);

    private sealed record Observation(ParsedCandidate Harder, ParsedCandidate Easier, double Weight);

    private sealed record MethodSlot(int MethodId, StimMethod Method, int Index);

    private sealed record FitState(double[] TimeRates, double[] EdgeRates);

    private enum OptimizerKind
    {
        BatchGradient,
        Momentum,
        Adam,
        RmsProp
    }

    private sealed record TrialConfig(string Name, OptimizerKind Kind, int Epochs, double LearningRate, double Regularization);

    public static OptimizationResult Optimize(IReadOnlyList<StimMethod> methods, IReadOnlyList<TuningComparisonEntry> history)
    {
        var stimHistory = history
            .Where(entry => string.Equals(entry.Mode, "Stim", StringComparison.Ordinal))
            .Select(entry => TryParseObservation(entry))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToList();

        if (stimHistory.Count < 25)
            throw new InvalidOperationException("Not enough stim comparison history to optimize multipliers yet.");

        var methodSlots = methods
            .OrderBy(method => method.Id)
            .Select((method, index) => new MethodSlot(method.Id, method, index))
            .ToList();

        var slotByMethodId = methodSlots.ToDictionary(slot => slot.MethodId);
        var observations = stimHistory
            .Where(obs => slotByMethodId.ContainsKey(obs.Harder.MethodId) && slotByMethodId.ContainsKey(obs.Easier.MethodId))
            .ToList();

        if (observations.Count < 25)
            throw new InvalidOperationException("Not enough parsed stim comparisons to optimize multipliers.");

        var seedTime = methodSlots.Select(slot => StimMethodPointMath.GetSeedMultipliers(slot.Method).TimePointsPerSecond).ToArray();
        var seedEdge = methodSlots.Select(slot => StimMethodPointMath.GetSeedMultipliers(slot.Method).EdgePointsPerEdge).ToArray();

        var shuffled = observations
            .OrderBy(_ => StableShuffleKey(_))
            .ToList();

        int validationCount = Math.Max(50, shuffled.Count / 5);
        if (validationCount >= shuffled.Count)
            validationCount = Math.Max(1, shuffled.Count / 4);

        var validation = shuffled.Take(validationCount).ToList();
        var training = shuffled.Skip(validationCount).ToList();
        if (training.Count == 0)
        {
            training = shuffled;
            validation = shuffled;
        }

        var trials = new[]
        {
            new TrialConfig("Adam", OptimizerKind.Adam, 300, 0.08, 0.0008),
            new TrialConfig("Momentum", OptimizerKind.Momentum, 320, 0.015, 0.0012),
            new TrialConfig("RMSProp", OptimizerKind.RmsProp, 300, 0.05, 0.0010),
            new TrialConfig("Batch Gradient", OptimizerKind.BatchGradient, 500, 0.18, 0.0015),
            new TrialConfig("Adam (Tighter Reg)", OptimizerKind.Adam, 320, 0.06, 0.0020)
        };

        var trialResults = new List<(TrialConfig Config, TrialResult Metrics, FitState State)>();
        foreach (var trial in trials)
        {
            var state = Fit(training, methodSlots, slotByMethodId, seedTime, seedEdge, trial);
            var metrics = Evaluate(state, validation, slotByMethodId);
            trialResults.Add((trial, new TrialResult(trial.Name, metrics.LogLoss, metrics.Accuracy), state));
        }

        var best = trialResults
            .OrderBy(result => result.Metrics.ValidationLogLoss)
            .ThenByDescending(result => result.Metrics.ValidationAccuracy)
            .First();

        var finalState = Fit(observations, methodSlots, slotByMethodId, seedTime, seedEdge, best.Config);
        NormalizeToLegacyScale(finalState, methodSlots);

        var multipliers = methodSlots.ToDictionary(
            slot => slot.MethodId,
            slot =>
            {
                int index = slot.Index;
                return (
                    Math.Max(MinTimePointsPerSecond, finalState.TimeRates[index]),
                    Math.Max(MinEdgePointsPerEdge, finalState.EdgeRates[index]));
            });

        return new OptimizationResult(
            best.Config.Name,
            best.Metrics.ValidationLogLoss,
            best.Metrics.ValidationAccuracy,
            trialResults
                .Select(result => result.Metrics)
                .OrderBy(result => result.ValidationLogLoss)
                .ThenByDescending(result => result.ValidationAccuracy)
                .ToList(),
            multipliers);
    }

    private static Observation? TryParseObservation(TuningComparisonEntry entry)
    {
        var harder = TryParseStimCandidate(entry.LoserKey);
        var easier = TryParseStimCandidate(entry.WinnerKey);
        if (harder is null || easier is null)
            return null;

        return new Observation(harder, easier, StrengthToWeight(entry.Strength));
    }

    private static ParsedCandidate? TryParseStimCandidate(string key)
    {
        if (!key.StartsWith("stim:", StringComparison.Ordinal))
            return null;

        var parts = key.Split(':');
        if (parts.Length != 4)
            return null;

        if (!int.TryParse(parts[1], out int methodId) || !int.TryParse(parts[3], out int amount))
            return null;

        bool isEdge = string.Equals(parts[2], "edge", StringComparison.Ordinal);
        bool isTime = string.Equals(parts[2], "time", StringComparison.Ordinal);
        if (!isEdge && !isTime)
            return null;

        return new ParsedCandidate(methodId, isEdge, amount);
    }

    private static double StableShuffleKey(Observation observation)
        => HashCode.Combine(
            observation.Harder.MethodId,
            observation.Harder.IsEdge,
            observation.Harder.Amount,
            observation.Easier.MethodId,
            observation.Easier.IsEdge,
            observation.Easier.Amount,
            observation.Weight,
            42);

    private static FitState Fit(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<MethodSlot> methodSlots,
        IReadOnlyDictionary<int, MethodSlot> slotByMethodId,
        double[] seedTime,
        double[] seedEdge,
        TrialConfig config)
    {
        var timeRates = seedTime.ToArray();
        var edgeRates = seedEdge.ToArray();
        var velocityTime = new double[timeRates.Length];
        var velocityEdge = new double[edgeRates.Length];
        var accumTime = new double[timeRates.Length];
        var accumEdge = new double[edgeRates.Length];
        var adamMTime = new double[timeRates.Length];
        var adamVTime = new double[timeRates.Length];
        var adamMEdge = new double[edgeRates.Length];
        var adamVEdge = new double[edgeRates.Length];
        int adamStep = 0;

        for (int epoch = 0; epoch < config.Epochs; epoch++)
        {
            if (config.Kind == OptimizerKind.BatchGradient)
            {
                var gradTime = new double[timeRates.Length];
                var gradEdge = new double[edgeRates.Length];

                foreach (var observation in observations)
                    AccumulateGradient(observation, slotByMethodId, timeRates, edgeRates, gradTime, gradEdge);

                ApplyRegularization(timeRates, edgeRates, seedTime, seedEdge, gradTime, gradEdge, config.Regularization);

                for (int i = 0; i < timeRates.Length; i++)
                {
                    timeRates[i] -= config.LearningRate * gradTime[i] / observations.Count;
                    edgeRates[i] -= config.LearningRate * gradEdge[i] / observations.Count;
                }
            }
            else
            {
                foreach (var observation in observations)
                {
                    var gradTime = new double[timeRates.Length];
                    var gradEdge = new double[edgeRates.Length];
                    AccumulateGradient(observation, slotByMethodId, timeRates, edgeRates, gradTime, gradEdge);
                    ApplyRegularization(timeRates, edgeRates, seedTime, seedEdge, gradTime, gradEdge, config.Regularization);

                    switch (config.Kind)
                    {
                        case OptimizerKind.Momentum:
                            for (int i = 0; i < timeRates.Length; i++)
                            {
                                velocityTime[i] = (0.9 * velocityTime[i]) + (config.LearningRate * gradTime[i]);
                                velocityEdge[i] = (0.9 * velocityEdge[i]) + (config.LearningRate * gradEdge[i]);
                                timeRates[i] -= velocityTime[i];
                                edgeRates[i] -= velocityEdge[i];
                            }
                            break;

                        case OptimizerKind.RmsProp:
                            for (int i = 0; i < timeRates.Length; i++)
                            {
                                accumTime[i] = (0.9 * accumTime[i]) + (0.1 * gradTime[i] * gradTime[i]);
                                accumEdge[i] = (0.9 * accumEdge[i]) + (0.1 * gradEdge[i] * gradEdge[i]);
                                timeRates[i] -= config.LearningRate * gradTime[i] / (Math.Sqrt(accumTime[i]) + 1e-8);
                                edgeRates[i] -= config.LearningRate * gradEdge[i] / (Math.Sqrt(accumEdge[i]) + 1e-8);
                            }
                            break;

                        case OptimizerKind.Adam:
                            adamStep++;
                            for (int i = 0; i < timeRates.Length; i++)
                            {
                                adamMTime[i] = (0.9 * adamMTime[i]) + (0.1 * gradTime[i]);
                                adamVTime[i] = (0.999 * adamVTime[i]) + (0.001 * gradTime[i] * gradTime[i]);
                                adamMEdge[i] = (0.9 * adamMEdge[i]) + (0.1 * gradEdge[i]);
                                adamVEdge[i] = (0.999 * adamVEdge[i]) + (0.001 * gradEdge[i] * gradEdge[i]);

                                double mHatTime = adamMTime[i] / (1.0 - Math.Pow(0.9, adamStep));
                                double vHatTime = adamVTime[i] / (1.0 - Math.Pow(0.999, adamStep));
                                double mHatEdge = adamMEdge[i] / (1.0 - Math.Pow(0.9, adamStep));
                                double vHatEdge = adamVEdge[i] / (1.0 - Math.Pow(0.999, adamStep));

                                timeRates[i] -= config.LearningRate * mHatTime / (Math.Sqrt(vHatTime) + 1e-8);
                                edgeRates[i] -= config.LearningRate * mHatEdge / (Math.Sqrt(vHatEdge) + 1e-8);
                            }
                            break;
                    }
                }
            }

            ClampRates(timeRates, edgeRates, methodSlots);
        }

        return new FitState(timeRates, edgeRates);
    }

    private static void AccumulateGradient(
        Observation observation,
        IReadOnlyDictionary<int, MethodSlot> slotByMethodId,
        double[] timeRates,
        double[] edgeRates,
        double[] gradTime,
        double[] gradEdge)
    {
        int harderIndex = slotByMethodId[observation.Harder.MethodId].Index;
        int easierIndex = slotByMethodId[observation.Easier.MethodId].Index;
        double harderScore = Score(observation.Harder, harderIndex, timeRates, edgeRates);
        double easierScore = Score(observation.Easier, easierIndex, timeRates, edgeRates);
        double probability = Sigmoid(harderScore - easierScore);
        double error = (probability - 1.0) * observation.Weight;

        if (observation.Harder.IsEdge) gradEdge[harderIndex] += error * observation.Harder.Amount;
        else gradTime[harderIndex] += error * observation.Harder.Amount;

        if (observation.Easier.IsEdge) gradEdge[easierIndex] -= error * observation.Easier.Amount;
        else gradTime[easierIndex] -= error * observation.Easier.Amount;
    }

    private static void ApplyRegularization(
        double[] timeRates,
        double[] edgeRates,
        double[] seedTime,
        double[] seedEdge,
        double[] gradTime,
        double[] gradEdge,
        double lambda)
    {
        for (int i = 0; i < timeRates.Length; i++)
        {
            gradTime[i] += lambda * (timeRates[i] - seedTime[i]);
            gradEdge[i] += lambda * (edgeRates[i] - seedEdge[i]);
        }
    }

    private static void ClampRates(double[] timeRates, double[] edgeRates, IReadOnlyList<MethodSlot> methodSlots)
    {
        for (int i = 0; i < methodSlots.Count; i++)
        {
            timeRates[i] = Math.Max(MinTimePointsPerSecond, timeRates[i]);
            edgeRates[i] = Math.Max(MinEdgePointsPerEdge, edgeRates[i]);
        }
    }

    private static void NormalizeToLegacyScale(FitState state, IReadOnlyList<MethodSlot> methodSlots)
    {
        var optimizedPoints = new List<double>();
        var legacyPoints = new List<double>();

        foreach (var slot in methodSlots)
        {
            int index = slot.Index;
            for (int seconds = 15; seconds <= 120; seconds += 15)
            {
                optimizedPoints.Add(seconds * state.TimeRates[index]);
                legacyPoints.Add(StimMethodPointMath.EstimateLegacyTimedPoints(slot.Method, seconds));
            }

            if (slot.Method.Closeness >= 3)
            {
                for (int edges = 1; edges <= 5; edges++)
                {
                    optimizedPoints.Add(edges * state.EdgeRates[index]);
                    legacyPoints.Add(StimMethodPointMath.EstimateLegacyEdgePoints(edges));
                }
            }
        }

        double optimizedMedian = Median(optimizedPoints);
        double legacyMedian = Median(legacyPoints);
        if (optimizedMedian <= 0 || legacyMedian <= 0)
            return;

        double scale = legacyMedian / optimizedMedian;
        for (int i = 0; i < state.TimeRates.Length; i++)
        {
            state.TimeRates[i] = Math.Max(MinTimePointsPerSecond, state.TimeRates[i] * scale);
            state.EdgeRates[i] = Math.Max(MinEdgePointsPerEdge, state.EdgeRates[i] * scale);
        }
    }

    private static (double LogLoss, double Accuracy) Evaluate(
        FitState state,
        IReadOnlyList<Observation> observations,
        IReadOnlyDictionary<int, MethodSlot> slotByMethodId)
    {
        double weightedLoss = 0;
        double weightedCorrect = 0;
        double totalWeight = 0;

        foreach (var observation in observations)
        {
            int harderIndex = slotByMethodId[observation.Harder.MethodId].Index;
            int easierIndex = slotByMethodId[observation.Easier.MethodId].Index;
            double probability = Sigmoid(
                Score(observation.Harder, harderIndex, state.TimeRates, state.EdgeRates)
                - Score(observation.Easier, easierIndex, state.TimeRates, state.EdgeRates));
            probability = Math.Clamp(probability, ProbabilityEpsilon, 1.0 - ProbabilityEpsilon);
            weightedLoss += -Math.Log(probability) * observation.Weight;
            if (probability >= 0.5)
                weightedCorrect += observation.Weight;
            totalWeight += observation.Weight;
        }

        if (totalWeight <= 0)
            return (0, 0);

        return (weightedLoss / totalWeight, weightedCorrect / totalWeight);
    }

    private static double Score(ParsedCandidate candidate, int index, double[] timeRates, double[] edgeRates)
        => candidate.Amount * (candidate.IsEdge ? edgeRates[index] : timeRates[index]);

    private static double Sigmoid(double value)
    {
        if (value >= 0)
        {
            double z = Math.Exp(-value);
            return 1.0 / (1.0 + z);
        }

        double ez = Math.Exp(value);
        return ez / (1.0 + ez);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        values.Sort();
        int middle = values.Count / 2;
        if ((values.Count % 2) == 1)
            return values[middle];
        return (values[middle - 1] + values[middle]) / 2.0;
    }

    private static double StrengthToWeight(int strength) => strength switch
    {
        1 => 0.5,
        3 => 2.0,
        _ => 1.0
    };
}