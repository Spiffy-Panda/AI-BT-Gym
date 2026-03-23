// ─────────────────────────────────────────────────────────────────────────────
// EloCalculator.cs — Standard ELO rating calculator
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace AiBtGym.Simulation;

public static class EloCalculator
{
    private const float K = 32f;

    /// <summary>
    /// Compute new ELO ratings after a match.
    /// score: 1.0 = player A wins, 0.0 = player B wins, 0.5 = draw.
    /// </summary>
    public static (float newA, float newB) Calculate(float ratingA, float ratingB, float scoreA)
    {
        float scoreB = 1f - scoreA;
        float expectedA = Expected(ratingA, ratingB);
        float expectedB = 1f - expectedA;

        float newA = ratingA + K * (scoreA - expectedA);
        float newB = ratingB + K * (scoreB - expectedB);
        return (newA, newB);
    }

    private static float Expected(float ratingA, float ratingB) =>
        1f / (1f + MathF.Pow(10f, (ratingB - ratingA) / 400f));
}
