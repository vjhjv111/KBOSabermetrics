namespace NaverSabermetrics.Web;

public sealed record StrengthTeam(string Code, int W, int D, int L, double RF, double RA)
{
    public int G => W + D + L;
}

public sealed record ForecastParameters(double OpponentWeight, double PriorGames, double HomeLogOdds);

/// <summary>Season-to-date Pythagorean strength, adjusted for opponents, venue and sample size.</summary>
public static class PlayoffStrengthModel
{
    const double Exponent = 1.83;
    const double ProbabilityFloor = 0.000001;

    /// <param name="homeGames">homeGames[i,j] counts completed games hosted by team i against team j.</param>
    public static double[] Estimate(IReadOnlyList<StrengthTeam> teams, int[,] homeGames, ForecastParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(homeGames);
        Validate(parameters);
        var count = teams.Count;
        if (count != 10 || homeGames.GetLength(0) != count || homeGames.GetLength(1) != count)
            throw new ArgumentException("Expected ten teams and a matching home-game matrix.");

        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var team = teams[i];
            if (team is null || string.IsNullOrWhiteSpace(team.Code) || !codes.Add(team.Code)
                || team.W < 0 || team.D < 0 || team.L < 0 || (long)team.W + team.D + team.L > int.MaxValue
                || !double.IsFinite(team.RF) || !double.IsFinite(team.RA) || team.RF < 0 || team.RA < 0
                || (team.G == 0 ? team.RF != 0 || team.RA != 0 : team.RF == 0 && team.RA == 0))
                throw new ArgumentException("Invalid team record or run totals.", nameof(teams));

            long completed = 0;
            for (var j = 0; j < count; j++)
            {
                if (homeGames[i, j] < 0 || homeGames[j, i] < 0 || (i == j && homeGames[i, j] != 0))
                    throw new ArgumentException("Invalid home-game count.", nameof(homeGames));
                completed += (long)homeGames[i, j] + homeGames[j, i];
            }
            if (completed != team.G)
                throw new ArgumentException("Completed home and away games do not match the team record.", nameof(homeGames));
        }

        var matrix = new double[count, count];
        var rhs = new double[count];
        for (var i = 0; i < count; i++)
        {
            var team = teams[i];
            matrix[i, i] = 1;
            if (team.G == 0) continue;

            var weight = team.G / (team.G + parameters.PriorGames);
            long balance = 0;
            for (var j = 0; j < count; j++)
            {
                balance += (long)homeGames[i, j] - homeGames[j, i];
                if (i != j)
                    matrix[i, j] = -weight * parameters.OpponentWeight
                        * ((long)homeGames[i, j] + homeGames[j, i]) / team.G;
            }
            rhs[i] = weight * (RawLogOdds(team.RF, team.RA) - parameters.HomeLogOdds * ((double)balance / team.G));
            if (!double.IsFinite(rhs[i])) throw new ArgumentException("Strength parameters exceed the numeric range.", nameof(parameters));
        }

        var ratings = Solve(matrix, rhs);
        var mean = ratings.Sum(x => x / count);
        for (var i = 0; i < count; i++)
        {
            ratings[i] -= mean;
            if (!double.IsFinite(ratings[i])) throw new ArgumentException("Unable to calculate finite team strengths.");
        }
        return ratings;
    }

    /// <summary>Home win probability conditional on the game having a winner.</summary>
    public static double Probability(double[] ratings, int home, int away, ForecastParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        Validate(parameters);
        if (home < 0 || home >= ratings.Length || away < 0 || away >= ratings.Length || home == away)
            throw new ArgumentException("Expected two distinct team indices.");
        if (ratings.Any(x => !double.IsFinite(x))) throw new ArgumentException("Team strengths must be finite.", nameof(ratings));
        var logOdds = ratings[home] - ratings[away] + parameters.HomeLogOdds;
        // The two branches avoid exponent overflow for extreme, but finite, inputs.
        if (logOdds >= 0) return 1 / (1 + Math.Exp(-logOdds));
        var odds = Math.Exp(logOdds);
        return odds / (1 + odds);
    }

    static void Validate(ForecastParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!double.IsFinite(parameters.OpponentWeight) || parameters.OpponentWeight is < 0 or > 1
            || !double.IsFinite(parameters.PriorGames) || parameters.PriorGames < 0
            || !double.IsFinite(parameters.HomeLogOdds)
            || (parameters.PriorGames == 0 && parameters.OpponentWeight == 1))
            throw new ArgumentException("Invalid or singular strength parameters.", nameof(parameters));
    }

    static double RawLogOdds(double runsFor, double runsAgainst)
    {
        // Normalize before exponentiation: identical Pythagorean probability, without overflow.
        var scale = Math.Max(runsFor, runsAgainst);
        var scored = Math.Pow(runsFor / scale, Exponent);
        var allowed = Math.Pow(runsAgainst / scale, Exponent);
        var pyth = Math.Clamp(scored / (scored + allowed), ProbabilityFloor, 1 - ProbabilityFloor);
        return Math.Log(pyth / (1 - pyth));
    }

    static double[] Solve(double[,] matrix, double[] rhs)
    {
        var count = rhs.Length;
        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < count; row++)
                if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
            if (Math.Abs(matrix[pivot, column]) < 1e-12)
                throw new ArgumentException("The strength system is singular or numerically unstable.");
            if (pivot != column)
            {
                for (var j = column; j < count; j++)
                    (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);
                (rhs[column], rhs[pivot]) = (rhs[pivot], rhs[column]);
            }
            for (var row = column + 1; row < count; row++)
            {
                var factor = matrix[row, column] / matrix[column, column];
                matrix[row, column] = 0;
                for (var j = column + 1; j < count; j++) matrix[row, j] -= factor * matrix[column, j];
                rhs[row] -= factor * rhs[column];
            }
        }
        var result = new double[count];
        for (var row = count - 1; row >= 0; row--)
        {
            var value = rhs[row];
            for (var j = row + 1; j < count; j++) value -= matrix[row, j] * result[j];
            result[row] = value / matrix[row, row];
            if (!double.IsFinite(result[row])) throw new ArgumentException("Unable to solve finite team strengths.");
        }
        return result;
    }
}
