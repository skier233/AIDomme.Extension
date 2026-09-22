using AIDome.Data;

namespace AIDome.Services;

public static class StimMethodPointMath
{
    public static (double TimePointsPerSecond, double EdgePointsPerEdge) GetSeedMultipliers(StimMethod method)
    {
        if (method.TimePointMultiplier > 0 || method.EdgePointMultiplier > 0)
        {
            return (
                method.TimePointMultiplier > 0 ? method.TimePointMultiplier : EstimateTimePointsPerSecond(method),
                method.EdgePointMultiplier > 0 ? method.EdgePointMultiplier : EstimateEdgePointsPerEdge(method));
        }

        return (EstimateTimePointsPerSecond(method), EstimateEdgePointsPerEdge(method));
    }

    public static double EstimateTimePointsPerSecond(StimMethod method)
    {
        double rate = 0.035
            + method.Intensity * 0.007
            + method.TortLevel * 0.005
            + method.Closeness * 0.004
            - method.Tease * 0.0015
            + (method.RequiresLube ? 0.003 : 0)
            + (method.ToyCategory != ToyCategory.None ? 0.006 : 0);

        return Math.Max(0.02, rate);
    }

    public static double EstimateEdgePointsPerEdge(StimMethod method)
    {
        double rate = 1.2
            + method.Intensity * 0.16
            + method.TortLevel * 0.13
            + method.Closeness * 0.12
            - method.Tease * 0.03
            + (method.RequiresLube ? 0.08 : 0)
            + (method.ToyCategory != ToyCategory.None ? 0.12 : 0);

        return Math.Max(0.8, rate);
    }

    public static int EstimateLegacyTimedPoints(StimMethod method, int durationSeconds)
    {
        int pts = Math.Max(1, durationSeconds / 20 + method.Intensity / 3 + method.Closeness / 4);
        if (method.TortLevel >= 5)
            pts += method.TortLevel / 3;
        return pts;
    }

    public static int EstimateLegacyEdgePoints(int edgeCount, int holdSeconds = 0)
    {
        int pts = edgeCount * 3;
        if (holdSeconds > 0)
            pts += holdSeconds / 5 + 1;
        return pts;
    }
}