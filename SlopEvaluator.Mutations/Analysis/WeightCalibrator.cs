using SlopEvaluator.Mutations.Models;
using SlopEvaluator.Mutations.Services;
using SlopEvaluator.Mutations.Runners;

namespace SlopEvaluator.Mutations.Analysis;

/// <summary>
/// Trains test quality weights via Ordinary Least Squares regression.
/// Maps 16 quality attributes → mutation score to learn which attributes
/// best predict test effectiveness.
/// </summary>
public static class WeightCalibrator
{
    /// <summary>
    /// Runs OLS regression on the provided data points to produce a weight profile.
    /// </summary>
    /// <param name="data">
    /// Training data where each point contains 16 quality attribute scores and the
    /// corresponding mutation score (dependent variable).
    /// </param>
    /// <returns>A <see cref="CalibrationResult"/> with the fitted weights, R-squared, MAE, and insights.</returns>
    /// <exception cref="InvalidOperationException">Thrown when fewer than 3 data points are provided.</exception>
    public static CalibrationResult Calibrate(List<CalibrationDataPoint> data)
    {
        if (data.Count < 3)
            throw new InvalidOperationException($"Need at least 3 data points, got {data.Count}");

        int n = data.Count;
        int p = QualityAttributes.Count; // 16

        // Build X matrix (n × p+1) with bias column
        var X = new double[n, p + 1];
        var y = new double[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < p; j++)
            {
                X[i, j] = data[i].AttributeScores[j];
            }
            X[i, p] = 1.0; // bias
            y[i] = data[i].MutationScore;
        }

        // Normal equation: w = (X^T X)^-1 X^T y
        var XtX = MatMul(Transpose(X, n, p + 1), X, p + 1, n, p + 1);
        var XtY = MatVecMul(Transpose(X, n, p + 1), y, p + 1, n);
        var w = Solve(XtX, XtY, p + 1);

        // Extract weights and bias
        var weights = new double[p];
        Array.Copy(w, weights, p);
        var bias = w[p];

        // Compute R²
        double yMean = y.Average();
        double ssTot = y.Sum(yi => (yi - yMean) * (yi - yMean));
        double ssRes = 0;
        for (int i = 0; i < n; i++)
        {
            double pred = bias;
            for (int j = 0; j < p; j++)
                pred += data[i].AttributeScores[j] * weights[j];
            ssRes += (y[i] - pred) * (y[i] - pred);
        }
        double rSquared = ssTot > 0 ? 1.0 - ssRes / ssTot : 0;

        // MAE
        double mae = 0;
        for (int i = 0; i < n; i++)
        {
            double pred = bias;
            for (int j = 0; j < p; j++)
                pred += data[i].AttributeScores[j] * weights[j];
            mae += Math.Abs(y[i] - pred);
        }
        mae /= n;

        // Generate insights
        var insights = GenerateInsights(weights);

        var profile = new WeightProfile
        {
            Version = 1,
            TrainedOn = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DataPoints = n,
            RSquared = Math.Round(rSquared, 4),
            Weights = weights.Select(w => Math.Round(w, 4)).ToArray(),
            Bias = Math.Round(bias, 4),
            Insights = insights
        };

        return new CalibrationResult
        {
            Profile = profile,
            Insights = insights,
            MeanAbsoluteError = Math.Round(mae, 2)
        };
    }

    private static List<string> GenerateInsights(double[] weights)
    {
        var insights = new List<string>();

        // Rank attributes by absolute weight
        var ranked = weights
            .Select((w, i) => (Name: QualityAttributes.Names[i], Weight: w, Index: i))
            .OrderByDescending(x => Math.Abs(x.Weight))
            .ToList();

        // Top 3
        var top3 = ranked.Take(3).ToList();
        insights.Add($"Top predictors: {string.Join(", ", top3.Select(x => $"{x.Name} ({x.Weight:+0.00;-0.00})"))}");

        // Bottom 3
        var bottom3 = ranked.TakeLast(3).ToList();
        insights.Add($"Lowest impact: {string.Join(", ", bottom3.Select(x => $"{x.Name} ({x.Weight:+0.00;-0.00})"))}");

        // Ratio of strongest to weakest
        if (Math.Abs(bottom3[0].Weight) > 0.001)
        {
            var ratio = Math.Abs(top3[0].Weight) / Math.Abs(bottom3[0].Weight);
            insights.Add($"{top3[0].Name} is {ratio:F1}x more predictive than {bottom3[0].Name}");
        }

        // Pillar comparison
        var pillarWeights = new double[4];
        for (int i = 0; i < weights.Length && i < 16; i++)
        {
            pillarWeights[i / 4] += Math.Abs(weights[i]);
        }
        var strongestPillar = QualityAttributes.PillarNames[Array.IndexOf(pillarWeights, pillarWeights.Max())];
        insights.Add($"Strongest pillar overall: {strongestPillar}");

        return insights;
    }

    // ── Matrix math (no dependencies) ────────────────────────────

    private static double[,] Transpose(double[,] A, int rows, int cols)
    {
        var result = new double[cols, rows];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                result[j, i] = A[i, j];
        return result;
    }

    private static double[,] MatMul(double[,] A, double[,] B, int aRows, int aCols, int bCols)
    {
        var result = new double[aRows, bCols];
        for (int i = 0; i < aRows; i++)
            for (int j = 0; j < bCols; j++)
                for (int k = 0; k < aCols; k++)
                    result[i, j] += A[i, k] * B[k, j];
        return result;
    }

    private static double[] MatVecMul(double[,] A, double[] v, int rows, int cols)
    {
        var result = new double[rows];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                result[i] += A[i, j] * v[j];
        return result;
    }

    /// <summary>
    /// Solve Ax = b using Gaussian elimination with partial pivoting.
    /// Adds ridge regularization (λ=0.01) to handle near-singular matrices.
    /// </summary>
    private static double[] Solve(double[,] A, double[] b, int n)
    {
        // Ridge regularization: A = A + λI
        const double lambda = 0.01;
        var aug = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = A[i, j] + (i == j ? lambda : 0);
            aug[i, n] = b[i];
        }

        // Forward elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            // Pivot
            int maxRow = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(aug[row, col]) > Math.Abs(aug[maxRow, col]))
                    maxRow = row;

            if (maxRow != col)
            {
                for (int j = 0; j <= n; j++)
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
            }

            if (Math.Abs(aug[col, col]) < 1e-12)
                continue; // Skip near-zero pivots

            for (int row = col + 1; row < n; row++)
            {
                double factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

        // Back substitution
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = aug[i, n];
            for (int j = i + 1; j < n; j++)
                x[i] -= aug[i, j] * x[j];
            if (Math.Abs(aug[i, i]) > 1e-12)
                x[i] /= aug[i, i];
        }

        return x;
    }
}
