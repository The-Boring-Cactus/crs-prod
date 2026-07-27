using FunctEngine;
using System;
using System.Linq;

namespace NonParametricFunctions
{
    [FunctEngineExport("Non-Parametric Tests", "Biblioteca de pruebas estadísticas no paramétricas")]
    public static class NonParametricLibrary
    {
        // ─── Rank-Based Two-Sample Tests ────────────────────────────────────────

        [FunctEngineExport("MannWhitneyUTest", "Prueba U de Mann-Whitney (equivalente a Wilcoxon rank-sum) para dos muestras independientes")]
        public static (double u, double zStatistic, double pValue) MannWhitneyUTest(double[] a, double[] b)
        {
            int n1 = a.Length, n2 = b.Length;
            if (n1 == 0 || n2 == 0)
                throw new ArgumentException("Ambos grupos deben tener al menos una observación");

            var combined = a.Concat(b).ToArray();
            var ranks = Rank(combined);

            double r1 = ranks.Take(n1).Sum();
            double u1 = r1 - n1 * (n1 + 1) / 2.0;
            double u2 = (double)n1 * n2 - u1;
            double u = Math.Min(u1, u2);

            int nTotal = n1 + n2;
            double meanU = n1 * n2 / 2.0;
            double tieSum = TieCorrectionSum(combined);
            double varU = (double)n1 * n2 / 12.0 * ((nTotal + 1) - tieSum / (nTotal * (nTotal - 1)));

            double z = varU > 0 ? (u1 - meanU) / Math.Sqrt(varU) : 0;
            double pValue = 2.0 * (1.0 - NormalCDF(Math.Abs(z)));

            return (u, z, pValue);
        }

        [FunctEngineExport("TwoSampleKsTest", "Prueba de Kolmogorov-Smirnov de dos muestras: compara las distribuciones de dos conjuntos de datos")]
        public static (double dStatistic, double pValue) TwoSampleKsTest(double[] a, double[] b)
        {
            int n1 = a.Length, n2 = b.Length;
            if (n1 == 0 || n2 == 0)
                throw new ArgumentException("Ambas muestras deben tener al menos una observación");

            var sortedA = a.OrderBy(x => x).ToArray();
            var sortedB = b.OrderBy(x => x).ToArray();
            var allValues = sortedA.Concat(sortedB).Distinct().OrderBy(x => x).ToArray();

            double d = 0;
            foreach (var v in allValues)
            {
                double cdfA = sortedA.Count(x => x <= v) / (double)n1;
                double cdfB = sortedB.Count(x => x <= v) / (double)n2;
                d = Math.Max(d, Math.Abs(cdfA - cdfB));
            }

            double nEff = (double)n1 * n2 / (n1 + n2);
            double pValue = KolmogorovAsymptoticPValue(nEff, d);

            return (d, pValue);
        }

        [FunctEngineExport("WilcoxonSignedRankTest", "Prueba de rangos con signo de Wilcoxon para datos pareados (x - y)")]
        public static (double wStatistic, double zStatistic, double pValue) WilcoxonSignedRankTest(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                throw new ArgumentException("Las muestras pareadas deben tener la misma longitud");

            var diffs = x.Zip(y, (xi, yi) => xi - yi).Where(d => d != 0).ToArray();
            int n = diffs.Length;
            if (n == 0)
                throw new ArgumentException("No hay diferencias distintas de cero");

            var absDiffs = diffs.Select(d => Math.Abs(d)).ToArray();
            var ranks = Rank(absDiffs);

            double wPlus = 0, wMinus = 0;
            for (int i = 0; i < n; i++)
            {
                if (diffs[i] > 0) wPlus += ranks[i];
                else wMinus += ranks[i];
            }
            double w = Math.Min(wPlus, wMinus);

            double meanW = n * (n + 1) / 4.0;
            double tieSum = TieCorrectionSum(absDiffs);
            double varW = n * (n + 1) * (2 * n + 1) / 24.0 - tieSum / 48.0;

            double z = varW > 0 ? (w - meanW) / Math.Sqrt(varW) : 0;
            double pValue = 2.0 * (1.0 - NormalCDF(Math.Abs(z)));

            return (w, z, pValue);
        }

        [FunctEngineExport("McNemarTest", "Prueba de McNemar (con corrección de continuidad) para una tabla 2x2 de datos pareados")]
        public static (double chiSquared, double pValue) McNemarTest(double[][] table)
        {
            if (table.Length != 2 || table[0].Length != 2 || table[1].Length != 2)
                throw new ArgumentException("Se requiere una tabla 2x2");

            double b = table[0][1];
            double c = table[1][0];
            if (b + c == 0)
                throw new ArgumentException("No hay pares discordantes (b + c = 0)");

            double chiSquared = Math.Pow(Math.Abs(b - c) - 1, 2) / (b + c);
            double pValue = ChiSquarePValue(chiSquared, 1);

            return (chiSquared, pValue);
        }

        // ─── Rank-Based Multi-Sample Tests ──────────────────────────────────────

        [FunctEngineExport("KruskalWallisTest", "Prueba H de Kruskal-Wallis: ANOVA no paramétrico de una vía para k grupos independientes")]
        public static (double hStatistic, int degreesOfFreedom, double pValue) KruskalWallisTest(double[][] groups)
        {
            int k = groups.Length;
            if (k < 2)
                throw new ArgumentException("Se requieren al menos 2 grupos");

            var combined = groups.SelectMany(g => g).ToArray();
            int nTotal = combined.Length;
            var ranks = Rank(combined);

            double h = 0;
            int offset = 0;
            foreach (var group in groups)
            {
                int ni = group.Length;
                if (ni == 0)
                    throw new ArgumentException("Cada grupo debe tener al menos una observación");

                double rankSum = 0;
                for (int i = 0; i < ni; i++) rankSum += ranks[offset + i];
                offset += ni;
                h += (rankSum * rankSum) / ni;
            }
            h = 12.0 / (nTotal * (nTotal + 1)) * h - 3 * (nTotal + 1);

            double tieSum = TieCorrectionSum(combined);
            double correction = 1 - tieSum / (Math.Pow(nTotal, 3) - nTotal);
            if (correction > 0) h /= correction;

            int df = k - 1;
            double pValue = ChiSquarePValue(h, df);

            return (h, df, pValue);
        }

        [FunctEngineExport("FriedmanTest", "Prueba de Friedman: ANOVA no paramétrico de dos vías por rangos para medidas repetidas (filas=bloques, columnas=tratamientos)")]
        public static (double chiSquared, int degreesOfFreedom, double pValue) FriedmanTest(double[][] data)
        {
            int n = data.Length;
            if (n < 2)
                throw new ArgumentException("Se requieren al menos 2 bloques (filas)");
            int k = data[0].Length;
            if (k < 2)
                throw new ArgumentException("Se requieren al menos 2 tratamientos (columnas)");

            var rankSums = new double[k];
            foreach (var row in data)
            {
                if (row.Length != k)
                    throw new ArgumentException("Todas las filas deben tener el mismo número de columnas");

                var ranks = Rank(row);
                for (int j = 0; j < k; j++) rankSums[j] += ranks[j];
            }

            double sumSq = rankSums.Sum(r => r * r);
            double chiSquared = (12.0 / (n * k * (k + 1))) * sumSq - 3.0 * n * (k + 1);

            int df = k - 1;
            double pValue = ChiSquarePValue(chiSquared, df);

            return (chiSquared, df, pValue);
        }

        // ─── Correlation ────────────────────────────────────────────────────────

        [FunctEngineExport("SpearmanRankCorrelation", "Coeficiente de correlación de rangos de Spearman (rho) y su valor p")]
        public static (double rho, double pValue) SpearmanRankCorrelation(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                throw new ArgumentException("Las muestras deben tener la misma longitud");
            int n = x.Length;
            if (n < 3)
                throw new ArgumentException("Se requieren al menos 3 observaciones");

            var rankX = Rank(x);
            var rankY = Rank(y);

            double meanRX = rankX.Average();
            double meanRY = rankY.Average();

            double cov = 0, varX = 0, varY = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = rankX[i] - meanRX;
                double dy = rankY[i] - meanRY;
                cov += dx * dy;
                varX += dx * dx;
                varY += dy * dy;
            }

            double denom = Math.Sqrt(varX * varY);
            double rho = denom > 0 ? cov / denom : 0;

            double denomT = 1 - rho * rho;
            double pValue;
            if (denomT <= 0)
            {
                pValue = 0;
            }
            else
            {
                double t = rho * Math.Sqrt((n - 2) / denomT);
                pValue = TDistPValue(Math.Abs(t), n - 2);
            }

            return (rho, pValue);
        }

        [FunctEngineExport("KendallTau", "Tau-b de Kendall (ajustada por empates) con valor p por aproximación normal")]
        public static (double tau, double zStatistic, double pValue) KendallTau(double[] x, double[] y)
        {
            if (x.Length != y.Length)
                throw new ArgumentException("Las muestras deben tener la misma longitud");
            int n = x.Length;
            if (n < 3)
                throw new ArgumentException("Se requieren al menos 3 observaciones");

            long concordant = 0, discordant = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double signX = Math.Sign(x[i] - x[j]);
                    double signY = Math.Sign(y[i] - y[j]);
                    double product = signX * signY;
                    if (product > 0) concordant++;
                    else if (product < 0) discordant++;
                }
            }

            double n0 = n * (n - 1) / 2.0;
            double tiesX = TieBreakdown(x);
            double tiesY = TieBreakdown(y);

            double denom = Math.Sqrt((n0 - tiesX) * (n0 - tiesY));
            double tau = denom > 0 ? (concordant - discordant) / denom : 0;

            double varTau = 2.0 * (2 * n + 5) / (9.0 * n * (n - 1));
            double z = varTau > 0 ? tau / Math.Sqrt(varTau) : 0;
            double pValue = 2.0 * (1.0 - NormalCDF(Math.Abs(z)));

            return (tau, z, pValue);
        }

        // ─── Contingency Tables & Goodness-of-Fit ──────────────────────────────

        [FunctEngineExport("ChiSquareIndependenceTest", "Prueba de chi-cuadrado de independencia para una tabla de contingencia r x c")]
        public static (double chiSquared, int degreesOfFreedom, double pValue) ChiSquareIndependenceTest(double[][] contingencyTable)
        {
            int rows = contingencyTable.Length;
            if (rows < 2)
                throw new ArgumentException("Se requieren al menos 2 filas");
            int cols = contingencyTable[0].Length;
            if (cols < 2)
                throw new ArgumentException("Se requieren al menos 2 columnas");

            var rowTotals = new double[rows];
            var colTotals = new double[cols];
            double grandTotal = 0;

            for (int i = 0; i < rows; i++)
            {
                if (contingencyTable[i].Length != cols)
                    throw new ArgumentException("Todas las filas deben tener el mismo número de columnas");

                for (int j = 0; j < cols; j++)
                {
                    rowTotals[i] += contingencyTable[i][j];
                    colTotals[j] += contingencyTable[i][j];
                    grandTotal += contingencyTable[i][j];
                }
            }
            if (grandTotal == 0)
                throw new ArgumentException("El total de la tabla no puede ser cero");

            double chiSquared = 0;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double expected = rowTotals[i] * colTotals[j] / grandTotal;
                    if (expected > 0)
                        chiSquared += Math.Pow(contingencyTable[i][j] - expected, 2) / expected;
                }
            }

            int df = (rows - 1) * (cols - 1);
            double pValue = ChiSquarePValue(chiSquared, df);

            return (chiSquared, df, pValue);
        }

        [FunctEngineExport("OneSampleKsTest", "Prueba de Kolmogorov-Smirnov de una muestra contra una distribución Normal(mean, stdDev)")]
        public static (double dStatistic, double pValue) OneSampleKsTest(double[] data, double mean, double stdDev)
        {
            if (stdDev <= 0)
                throw new ArgumentException("La desviación estándar debe ser positiva");
            int n = data.Length;
            if (n < 2)
                throw new ArgumentException("Se requieren al menos 2 observaciones");

            var sorted = data.OrderBy(v => v).ToArray();
            double d = 0;
            for (int i = 0; i < n; i++)
            {
                double empiricalBefore = (double)i / n;
                double empiricalAfter = (double)(i + 1) / n;
                double theoretical = NormalCDF((sorted[i] - mean) / stdDev);
                d = Math.Max(d, Math.Max(Math.Abs(theoretical - empiricalBefore), Math.Abs(theoretical - empiricalAfter)));
            }

            double pValue = KolmogorovAsymptoticPValue(n, d);

            return (d, pValue);
        }

        // ─── Categorical / Contingency-Table Analysis───

        [FunctEngineExport("FrequencyTable", "Tabla de frecuencias de una variable categórica: valor, conteo y porcentaje ")]
        public static Dictionary<string, object>[] FrequencyTable(string[] categories)
        {
            if (categories.Length == 0)
                throw new ArgumentException("Se requiere al menos una observación");

            int total = categories.Length;
            return categories.GroupBy(c => c)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Dictionary<string, object>
                {
                    ["value"] = g.Key,
                    ["count"] = g.Count(),
                    ["percent"] = g.Count() * 100.0 / total
                })
                .ToArray();
        }

        [FunctEngineExport("CrossTab", "Tabla de contingencia (crosstab) entre dos variables categóricas, con conteos y porcentajes por fila/columna/total ")]
        public static Dictionary<string, object> CrossTab(string[] rowCategories, string[] colCategories)
        {
            if (rowCategories.Length != colCategories.Length)
                throw new ArgumentException("Las dos variables deben tener la misma longitud");
            if (rowCategories.Length == 0)
                throw new ArgumentException("Se requiere al menos una observación");

            var rowLabels = rowCategories.Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
            var colLabels = colCategories.Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
            int rows = rowLabels.Length, cols = colLabels.Length;

            double[][] counts = new double[rows][];
            for (int i = 0; i < rows; i++) counts[i] = new double[cols];

            for (int k = 0; k < rowCategories.Length; k++)
            {
                int i = Array.IndexOf(rowLabels, rowCategories[k]);
                int j = Array.IndexOf(colLabels, colCategories[k]);
                counts[i][j]++;
            }

            double[] rowTotals = counts.Select(r => r.Sum()).ToArray();
            double[] colTotals = new double[cols];
            for (int j = 0; j < cols; j++) colTotals[j] = counts.Sum(r => r[j]);
            double grandTotal = rowTotals.Sum();

            double[][] rowPercent = counts.Select((r, i) => r.Select(v => rowTotals[i] > 0 ? v * 100.0 / rowTotals[i] : 0).ToArray()).ToArray();
            double[][] colPercent = counts.Select(r => r.Select((v, j) => colTotals[j] > 0 ? v * 100.0 / colTotals[j] : 0).ToArray()).ToArray();
            double[][] totalPercent = counts.Select(r => r.Select(v => grandTotal > 0 ? v * 100.0 / grandTotal : 0).ToArray()).ToArray();

            // Los arreglos anidados dentro de un Dictionary no se convierten
            // automáticamente a List<object> (a diferencia de arreglos/tuplas de
            // nivel superior), así que ArrayGet() no podría indexarlos sin este
            // envoltorio explícito.
            return new Dictionary<string, object>
            {
                ["rowLabels"] = ToScriptArray(rowLabels),
                ["colLabels"] = ToScriptArray(colLabels),
                ["counts"] = ToScriptArray(counts),
                ["rowPercent"] = ToScriptArray(rowPercent),
                ["colPercent"] = ToScriptArray(colPercent),
                ["totalPercent"] = ToScriptArray(totalPercent),
                ["rowTotals"] = ToScriptArray(rowTotals),
                ["colTotals"] = ToScriptArray(colTotals),
                ["grandTotal"] = grandTotal
            };
        }

        private static List<object> ToScriptArray(Array arr)
        {
            var list = new List<object>();
            foreach (var item in arr)
                list.Add(item is Array nested ? ToScriptArray(nested) : item);
            return list;
        }

        [FunctEngineExport("FishersExactTest", "Prueba exacta de Fisher para una tabla 2x2 (alternativa a chi-cuadrado en muestras pequeñas): p-value bilateral exacto y odds ratio")]
        public static (double pValue, double oddsRatio) FishersExactTest(double[][] table2x2)
        {
            if (table2x2.Length != 2 || table2x2[0].Length != 2 || table2x2[1].Length != 2)
                throw new ArgumentException("Se requiere una tabla 2x2");

            int a = (int)Math.Round(table2x2[0][0]);
            int b = (int)Math.Round(table2x2[0][1]);
            int c = (int)Math.Round(table2x2[1][0]);
            int d = (int)Math.Round(table2x2[1][1]);

            int row1 = a + b, row2 = c + d, col1 = a + c, n = a + b + c + d;

            double LogHyperProb(int x)
            {
                int y1 = row1 - x, y2 = col1 - x, y3 = row2 - col1 + x;
                if (x < 0 || y1 < 0 || y2 < 0 || y3 < 0) return double.NegativeInfinity;
                return LogChoose(row1, x) + LogChoose(row2, col1 - x) - LogChoose(n, col1);
            }

            double LogChoose(int nn, int kk)
            {
                if (kk < 0 || kk > nn) return double.NegativeInfinity;
                return LogGamma(nn + 1) - LogGamma(kk + 1) - LogGamma(nn - kk + 1);
            }

            double observedLogP = LogHyperProb(a);
            int lo = Math.Max(0, col1 - row2);
            int hi = Math.Min(row1, col1);

            double pValue = 0;
            for (int x = lo; x <= hi; x++)
            {
                double logP = LogHyperProb(x);
                if (logP <= observedLogP + 1e-7)
                    pValue += Math.Exp(logP);
            }
            pValue = Math.Min(1.0, pValue);

            double oddsRatio = (b == 0 || c == 0) ? (a == 0 || d == 0 ? double.NaN : double.PositiveInfinity) : ((double)a * d) / (b * c);

            return (pValue, oddsRatio);
        }

        [FunctEngineExport("OddsRatio", "Odds ratio e intervalo de confianza del 95% para una tabla 2x2")]
        public static (double oddsRatio, double lower95, double upper95) OddsRatio(double[][] table2x2)
        {
            if (table2x2.Length != 2 || table2x2[0].Length != 2 || table2x2[1].Length != 2)
                throw new ArgumentException("Se requiere una tabla 2x2");

            double a = table2x2[0][0], b = table2x2[0][1], c = table2x2[1][0], d = table2x2[1][1];
            if (a <= 0 || b <= 0 || c <= 0 || d <= 0)
                throw new ArgumentException("Todas las celdas deben ser mayores que cero (use una corrección de continuidad si hay ceros)");

            double or = (a * d) / (b * c);
            double logOr = Math.Log(or);
            double se = Math.Sqrt(1 / a + 1 / b + 1 / c + 1 / d);

            return (or, Math.Exp(logOr - 1.96 * se), Math.Exp(logOr + 1.96 * se));
        }

        [FunctEngineExport("RelativeRisk", "Riesgo relativo (razón de riesgos) e intervalo de confianza del 95% para una tabla 2x2 (filas=exposición, columnas=desenlace)")]
        public static (double relativeRisk, double lower95, double upper95) RelativeRisk(double[][] table2x2)
        {
            if (table2x2.Length != 2 || table2x2[0].Length != 2 || table2x2[1].Length != 2)
                throw new ArgumentException("Se requiere una tabla 2x2");

            double a = table2x2[0][0], b = table2x2[0][1], c = table2x2[1][0], d = table2x2[1][1];
            if (a + b <= 0 || c + d <= 0 || a <= 0 || c <= 0)
                throw new ArgumentException("Cada grupo de exposición debe tener al menos un evento y una observación");

            double riskExposed = a / (a + b);
            double riskUnexposed = c / (c + d);
            double rr = riskExposed / riskUnexposed;

            double logRr = Math.Log(rr);
            double se = Math.Sqrt(1 / a - 1 / (a + b) + 1 / c - 1 / (c + d));

            return (rr, Math.Exp(logRr - 1.96 * se), Math.Exp(logRr + 1.96 * se));
        }

        // ─── Normality & Outlier Detection ───────

        [FunctEngineExport("ShapiroWilkTest", "Prueba de normalidad de Shapiro-Wilk (aproximación de Royston, AS R94): estadístico W y p-value")]
        public static (double wStatistic, double pValue) ShapiroWilkTest(double[] data)
        {
            int n = data.Length;
            if (n < 3 || n > 5000)
                throw new ArgumentException("Se requieren entre 3 y 5000 observaciones");

            var sorted = data.OrderBy(x => x).ToArray();
            double mean = sorted.Average();

            // Puntuaciones normales esperadas (aproximación de Blom)
            double[] m = new double[n];
            for (int i = 0; i < n; i++)
                m[i] = InverseNormalCDF((i + 1 - 0.375) / (n + 0.25));

            double sumM2 = m.Sum(v => v * v);
            double[] c = m.Select(v => v / Math.Sqrt(sumM2)).ToArray();

            double[] a = new double[n];
            double u = 1.0 / Math.Sqrt(n);

            if (n <= 5)
            {
                double an = -2.706056 * Math.Pow(u, 5) + 4.434685 * Math.Pow(u, 4) - 2.071190 * Math.Pow(u, 3) - 0.147981 * u * u + 0.221157 * u + c[n - 1];
                for (int i = 0; i < n; i++) a[i] = (i == n - 1) ? an : (i == 0 ? -an : 0);
                double phi = (sumM2 - 2 * m[n - 1] * m[n - 1]) / (1 - 2 * an * an);
                for (int i = 1; i < n - 1; i++) a[i] = m[i] / Math.Sqrt(phi);
                a[0] = -an;
                a[n - 1] = an;
            }
            else
            {
                double an = -2.706056 * Math.Pow(u, 5) + 4.434685 * Math.Pow(u, 4) - 2.071190 * Math.Pow(u, 3) - 0.147981 * u * u + 0.221157 * u + c[n - 1];
                double an1 = -3.582633 * Math.Pow(u, 5) + 5.682633 * Math.Pow(u, 4) - 1.752461 * Math.Pow(u, 3) - 0.293762 * u * u + 0.042981 * u + c[n - 2];
                double phi = (sumM2 - 2 * m[n - 1] * m[n - 1] - 2 * m[n - 2] * m[n - 2]) / (1 - 2 * an * an - 2 * an1 * an1);
                for (int i = 2; i < n - 2; i++) a[i] = m[i] / Math.Sqrt(phi);
                a[0] = -an; a[n - 1] = an;
                a[1] = -an1; a[n - 2] = an1;
            }

            double num = 0;
            for (int i = 0; i < n; i++) num += a[i] * sorted[i];
            double denom = sorted.Sum(x => Math.Pow(x - mean, 2));
            double w = Math.Min(1.0, (num * num) / denom);

            double pValue;
            if (n <= 11)
            {
                double gamma = -2.273 + 0.459 * n;
                double w1 = -Math.Log(gamma - Math.Log(1 - w));
                double mu = 0.5440 - 0.39978 * n + 0.025054 * n * n - 0.0006714 * n * n * n;
                double sigma = Math.Exp(1.3822 - 0.77857 * n + 0.062767 * n * n - 0.0020322 * n * n * n);
                double z = (w1 - mu) / sigma;
                pValue = 1.0 - NormalCDF(z);
            }
            else
            {
                double lnN = Math.Log(n);
                double w1 = Math.Log(1 - w);
                double mu = -1.5861 - 0.31082 * lnN - 0.083751 * lnN * lnN + 0.0038915 * lnN * lnN * lnN;
                double sigma = Math.Exp(-0.4803 - 0.082676 * lnN + 0.0030302 * lnN * lnN);
                double z = (w1 - mu) / sigma;
                pValue = 1.0 - NormalCDF(z);
            }

            return (w, Math.Max(0, Math.Min(1, pValue)));
        }

        [FunctEngineExport("GrubbsTest", "Prueba de Grubbs para detectar un único valor atípico (outlier) en una muestra")]
        public static (double gStatistic, double pValue, int outlierIndex, bool isOutlier) GrubbsTest(double[] data, double alpha = 0.05)
        {
            int n = data.Length;
            if (n < 3)
                throw new ArgumentException("Se requieren al menos 3 observaciones");

            double mean = data.Average();
            double stdDev = Math.Sqrt(data.Sum(x => Math.Pow(x - mean, 2)) / (n - 1));
            if (stdDev == 0)
                return (0, 1, 0, false);

            double[] deviations = data.Select(x => Math.Abs(x - mean)).ToArray();
            double maxDev = deviations.Max();
            int outlierIndex = Array.IndexOf(deviations, maxDev);
            double g = maxDev / stdDev;

            double k = Math.Pow(g * Math.Sqrt(n) / (n - 1), 2);
            double pValue;
            if (k >= 1)
            {
                pValue = 0;
            }
            else
            {
                double t2 = k * (n - 2) / (1 - k);
                double t = Math.Sqrt(t2);
                double pOneSided = TDistPValue(t, n - 2) / 2.0;
                pValue = Math.Min(1.0, n * pOneSided);
            }

            return (g, pValue, outlierIndex, pValue < alpha);
        }

        [FunctEngineExport("IqrOutliers", "Detecta valores atípicos usando la regla del rango intercuartílico (IQR): valores fuera de Q1-k*IQR o Q3+k*IQR")]
        public static (double[] outliers, int[] outlierIndices, double lowerBound, double upperBound, double q1, double q3, double iqr) IqrOutliers(double[] data, double k = 1.5)
        {
            if (data.Length < 4)
                throw new ArgumentException("Se requieren al menos 4 observaciones");

            var sorted = data.OrderBy(x => x).ToArray();
            double q1 = PercentileOf(sorted, 25);
            double q3 = PercentileOf(sorted, 75);
            double iqr = q3 - q1;
            double lowerBound = q1 - k * iqr;
            double upperBound = q3 + k * iqr;

            var outlierIndices = Enumerable.Range(0, data.Length)
                .Where(i => data[i] < lowerBound || data[i] > upperBound)
                .ToArray();
            var outliers = outlierIndices.Select(i => data[i]).ToArray();

            return (outliers, outlierIndices, lowerBound, upperBound, q1, q3, iqr);
        }

        // ─── Ranking & Standardization  ──

        [FunctEngineExport("RankTransform", "Asigna rangos (1..n) a los valores, usando el rango promedio para empates ")]
        public static double[] RankTransform(double[] values) => Rank(values);

        [FunctEngineExport("NTile", "Asigna cada valor a un grupo de percentil/cuantil (1..tiles), por ejemplo 4 para cuartiles o 10 para deciles ")]
        public static int[] NTile(double[] values, int tiles)
        {
            if (tiles < 2)
                throw new ArgumentException("Se requieren al menos 2 grupos");
            int n = values.Length;
            var ranks = Rank(values);
            return ranks.Select(r =>
            {
                int bucket = (int)Math.Floor((r - 1) / n * tiles) + 1;
                return Math.Min(tiles, Math.Max(1, bucket));
            }).ToArray();
        }

        [FunctEngineExport("ZScoreNormalize", "Estandariza un arreglo a media 0 y desviación estándar 1 ")]
        public static double[] ZScoreNormalize(double[] values)
        {
            if (values.Length < 2)
                throw new ArgumentException("Se requieren al menos 2 valores");
            double mean = values.Average();
            double stdDev = Math.Sqrt(values.Sum(x => Math.Pow(x - mean, 2)) / (values.Length - 1));
            if (stdDev == 0)
                throw new ArgumentException("La desviación estándar es cero; no se puede normalizar");
            return values.Select(x => (x - mean) / stdDev).ToArray();
        }

        [FunctEngineExport("MinMaxNormalize", "Reescala un arreglo al rango [newMin, newMax] (por defecto [0, 1])")]
        public static double[] MinMaxNormalize(double[] values, double newMin = 0, double newMax = 1)
        {
            double min = values.Min(), max = values.Max();
            if (max == min)
                throw new ArgumentException("Todos los valores son iguales; no se puede normalizar");
            return values.Select(x => newMin + (x - min) * (newMax - newMin) / (max - min)).ToArray();
        }

        // ─── Survival Analysis  ──────────────────────

        [FunctEngineExport("KaplanMeier", "Estimador de Kaplan-Meier de la función de supervivencia, con error estándar de Greenwood e IC 95%")]
        public static (
            double[] times, int[] atRisk, int[] events, double[] survivalProbability,
            double[] standardError, double[] lower95, double[] upper95, double medianSurvivalTime
        ) KaplanMeier(double[] time, bool[] eventOccurred)
        {
            if (time.Length != eventOccurred.Length)
                throw new ArgumentException("Los arreglos de tiempo y evento deben tener la misma longitud");
            int n = time.Length;
            if (n == 0)
                throw new ArgumentException("Se requiere al menos una observación");

            // Los "escalones" de Kaplan-Meier solo ocurren en tiempos de evento; las
            // observaciones censuradas reducen el conjunto en riesgo pero no producen
            // un escalón propio.
            var uniqueEventTimes = time.Where((t, i) => eventOccurred[i]).Distinct().OrderBy(t => t).ToArray();

            double survival = 1.0;
            double greenwoodSum = 0;
            const double z95 = 1.959963985;

            var timesList = new List<double>();
            var atRiskList = new List<int>();
            var eventsList = new List<int>();
            var survivalList = new List<double>();
            var seList = new List<double>();
            var lowerList = new List<double>();
            var upperList = new List<double>();

            foreach (var t in uniqueEventTimes)
            {
                // En riesgo justo antes de t: todo sujeto (evento o censurado) con
                // tiempo observado >= t.
                int atRiskHere = time.Count(x => x >= t);
                if (atRiskHere == 0) continue;

                int eventsHere = 0;
                for (int i = 0; i < n; i++)
                    if (eventOccurred[i] && time[i] == t) eventsHere++;

                survival *= 1.0 - (double)eventsHere / atRiskHere;
                if (atRiskHere - eventsHere > 0)
                    greenwoodSum += (double)eventsHere / (atRiskHere * (double)(atRiskHere - eventsHere));

                double se = survival * Math.Sqrt(greenwoodSum);
                double lower = Math.Max(0, survival - z95 * se);
                double upper = Math.Min(1, survival + z95 * se);

                timesList.Add(t);
                atRiskList.Add(atRiskHere);
                eventsList.Add(eventsHere);
                survivalList.Add(survival);
                seList.Add(se);
                lowerList.Add(lower);
                upperList.Add(upper);
            }

            double medianSurvivalTime = double.NaN;
            for (int i = 0; i < survivalList.Count; i++)
            {
                if (survivalList[i] <= 0.5)
                {
                    medianSurvivalTime = timesList[i];
                    break;
                }
            }

            return (timesList.ToArray(), atRiskList.ToArray(), eventsList.ToArray(), survivalList.ToArray(),
                    seList.ToArray(), lowerList.ToArray(), upperList.ToArray(), medianSurvivalTime);
        }

        [FunctEngineExport("LogRankTest", "Prueba de rango logarítmico (Mantel-Haenszel) para comparar las curvas de supervivencia de dos grupos ")]
        public static (double chiSquared, double pValue) LogRankTest(double[] time1, bool[] event1, double[] time2, bool[] event2)
        {
            if (time1.Length != event1.Length || time2.Length != event2.Length)
                throw new ArgumentException("Los arreglos de tiempo y evento deben tener la misma longitud en cada grupo");
            if (time1.Length == 0 || time2.Length == 0)
                throw new ArgumentException("Ambos grupos deben tener al menos una observación");

            var allEventTimes = time1.Where((t, i) => event1[i])
                .Concat(time2.Where((t, i) => event2[i]))
                .Distinct().OrderBy(t => t).ToArray();

            double observed1 = 0, expected1 = 0, variance = 0;

            foreach (var t in allEventTimes)
            {
                int n1 = time1.Count(x => x >= t);
                int n2 = time2.Count(x => x >= t);
                int nTotal = n1 + n2;
                if (nTotal == 0) continue;

                int d1 = 0;
                for (int i = 0; i < time1.Length; i++)
                    if (event1[i] && time1[i] == t) d1++;
                int d2 = 0;
                for (int i = 0; i < time2.Length; i++)
                    if (event2[i] && time2[i] == t) d2++;
                int dTotal = d1 + d2;

                expected1 += dTotal * (double)n1 / nTotal;
                observed1 += d1;

                if (nTotal > 1)
                    variance += (double)dTotal * (nTotal - dTotal) * n1 * n2 / ((double)nTotal * nTotal * (nTotal - 1));
            }

            double chiSquared = variance > 0 ? Math.Pow(observed1 - expected1, 2) / variance : 0;
            double pValue = ChiSquarePValue(chiSquared, 1);

            return (chiSquared, pValue);
        }

        // ─── Random Sampling & Bootstrap  ────────

        [FunctEngineExport("RandomSample", "Extrae una muestra aleatoria simple, con o sin reemplazo ")]
        public static double[] RandomSample(double[] data, int sampleSize, bool withReplacement = false, int? seed = null)
        {
            if (sampleSize <= 0)
                throw new ArgumentException("El tamaño de muestra debe ser positivo");
            if (!withReplacement && sampleSize > data.Length)
                throw new ArgumentException("El tamaño de muestra no puede exceder el tamaño de los datos sin reemplazo");

            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            if (withReplacement)
                return Enumerable.Range(0, sampleSize).Select(_ => data[rng.Next(data.Length)]).ToArray();

            return data.OrderBy(_ => rng.Next()).Take(sampleSize).ToArray();
        }

        [FunctEngineExport("StratifiedSample", "Extrae una muestra aleatoria estratificada, tomando el mismo número de observaciones de cada estrato ")]
        public static Dictionary<string, object> StratifiedSample(double[] data, string[] strata, int sampleSizePerStratum, int? seed = null)
        {
            if (data.Length != strata.Length)
                throw new ArgumentException("Los arreglos de datos y estratos deben tener la misma longitud");

            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            var groups = data.Zip(strata, (d, s) => (d, s)).GroupBy(x => x.s);

            var result = new Dictionary<string, object>();
            foreach (var group in groups)
            {
                var values = group.Select(x => x.d).ToArray();
                int take = Math.Min(sampleSizePerStratum, values.Length);
                var sample = values.OrderBy(_ => rng.Next()).Take(take).ToArray();
                result[group.Key] = ToScriptArray(sample);
            }
            return result;
        }

        [FunctEngineExport("BootstrapConfidenceInterval", "Intervalo de confianza bootstrap (remuestreo con reemplazo) para la media, mediana o desviación estándar")]
        public static (double pointEstimate, double lower, double upper, double standardError) BootstrapConfidenceInterval(
            double[] data, string statistic = "mean", int numResamples = 1000, double confidence = 0.95, int? seed = null)
        {
            if (data.Length < 2)
                throw new ArgumentException("Se requieren al menos 2 observaciones");
            if (numResamples < 100)
                throw new ArgumentException("Se requieren al menos 100 remuestreos para una estimación razonable");

            Func<double[], double> computeStat = statistic.ToLower() switch
            {
                "mean" => arr => arr.Average(),
                "median" => arr => PercentileOf(arr.OrderBy(x => x).ToArray(), 50),
                "stddev" => arr => Math.Sqrt(arr.Sum(x => Math.Pow(x - arr.Average(), 2)) / (arr.Length - 1)),
                _ => throw new ArgumentException("El estadístico debe ser 'mean', 'median' o 'stddev'")
            };

            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            int n = data.Length;
            double pointEstimate = computeStat(data);

            var resampleStats = new double[numResamples];
            for (int r = 0; r < numResamples; r++)
            {
                var resample = new double[n];
                for (int i = 0; i < n; i++)
                    resample[i] = data[rng.Next(n)];
                resampleStats[r] = computeStat(resample);
            }

            Array.Sort(resampleStats);
            double alpha = 1.0 - confidence;
            double lower = PercentileOf(resampleStats, alpha / 2.0 * 100);
            double upper = PercentileOf(resampleStats, (1.0 - alpha / 2.0) * 100);
            double meanOfResamples = resampleStats.Average();
            double standardError = Math.Sqrt(resampleStats.Sum(x => Math.Pow(x - meanOfResamples, 2)) / (numResamples - 1));

            return (pointEstimate, lower, upper, standardError);
        }

        // ─── Internal helpers ───────────────────────────────────────────────────

        private static double PercentileOf(double[] sortedValues, double percentile)
        {
            int n = sortedValues.Length;
            if (n == 1) return sortedValues[0];
            double rank = (percentile / 100.0) * (n - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            if (lower == upper) return sortedValues[lower];
            double fraction = rank - lower;
            return sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]);
        }

        private static double InverseNormalCDF(double p)
        {
            // Algoritmo de Beasley-Springer-Moro
            double a0 = 2.515517, a1 = 0.802853, a2 = 0.010328;
            double b1 = 1.432788, b2 = 0.189269, b3 = 0.001308;
            double t = p < 0.5 ? Math.Sqrt(-2.0 * Math.Log(p)) : Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            double num = a0 + a1 * t + a2 * t * t;
            double den = 1.0 + b1 * t + b2 * t * t + b3 * t * t * t;
            double z = t - num / den;
            return p < 0.5 ? -z : z;
        }

        // Ranks values in ascending order, assigning the average rank to tied groups (1-based).
        private static double[] Rank(double[] values)
        {
            int n = values.Length;
            var indices = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
            var ranks = new double[n];

            int i = 0;
            while (i < n)
            {
                int j = i;
                while (j + 1 < n && values[indices[j + 1]] == values[indices[i]]) j++;
                double avgRank = (i + 1 + j + 1) / 2.0;
                for (int m = i; m <= j; m++) ranks[indices[m]] = avgRank;
                i = j + 1;
            }

            return ranks;
        }

        private static double TieCorrectionSum(double[] values)
        {
            return values.GroupBy(v => v)
                          .Where(g => g.Count() > 1)
                          .Sum(g => { double t = g.Count(); return t * t * t - t; });
        }

        private static double TieBreakdown(double[] values)
        {
            return values.GroupBy(v => v)
                          .Where(g => g.Count() > 1)
                          .Sum(g => { double t = g.Count(); return t * (t - 1) / 2.0; });
        }

        private static double KolmogorovAsymptoticPValue(double effectiveN, double d)
        {
            if (d <= 0) return 1.0;
            double lambda = (Math.Sqrt(effectiveN) + 0.12 + 0.11 / Math.Sqrt(effectiveN)) * d;

            double sum = 0;
            for (int k = 1; k <= 100; k++)
            {
                double term = (k % 2 == 1 ? 1.0 : -1.0) * Math.Exp(-2.0 * k * k * lambda * lambda);
                sum += term;
                if (Math.Abs(term) < 1e-10) break;
            }

            double p = 2.0 * sum;
            return Math.Max(0.0, Math.Min(1.0, p));
        }

        private static double NormalCDF(double z)
        {
            double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(z));
            double d = 0.3989422820 * Math.Exp(-z * z / 2.0);
            double poly = t * (0.3193815 + t * (-0.3565638 + t * (1.7814779 + t * (-1.8212560 + t * 1.3302744))));
            double p = 1.0 - d * poly;
            return z >= 0 ? p : 1.0 - p;
        }

        private static double LogGamma(double x)
        {
            double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 1.208650973866179e-3, -5.395239384953e-6 };
            double y = x;
            double tmp = x + 5.5 - (x + 0.5) * Math.Log(x + 5.5);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) ser += c[j] / ++y;
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        private static double BetaCF(double a, double b, double x)
        {
            const double fpmin = 1e-30;
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < fpmin) d = fpmin;
            d = 1.0 / d;
            double h = d;
            for (int mm = 1; mm <= 200; mm++)
            {
                int m2 = 2 * mm;
                double aa = mm * (b - mm) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d; h *= d * c;
                aa = -(a + mm) * (qab + mm) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) <= 3e-7) break;
            }
            return h;
        }

        private static double BetaInc(double a, double b, double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double lbeta = LogGamma(a + b) - LogGamma(a) - LogGamma(b);
            double bt = Math.Exp(lbeta + a * Math.Log(x) + b * Math.Log(1.0 - x));
            return x < (a + 1.0) / (a + b + 2.0)
                ? bt * BetaCF(a, b, x) / a
                : 1.0 - bt * BetaCF(b, a, 1.0 - x) / b;
        }

        private static double GammaIncSeries(double a, double x)
        {
            double sum = 1.0 / a, del = 1.0 / a, ap = a;
            for (int n = 1; n <= 200; n++) { ap++; del *= x / ap; sum += del; if (Math.Abs(del) < Math.Abs(sum) * 3e-7) break; }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        private static double GammaIncCF(double a, double x)
        {
            const double fpmin = 1e-30;
            double b = x + 1.0 - a, c = 1.0 / fpmin, d = 1.0 / b, h = d;
            for (int i = 1; i <= 200; i++)
            {
                double an = -i * (i - a); b += 2.0;
                d = an * d + b; if (Math.Abs(d) < fpmin) d = fpmin;
                c = b + an / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d; double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) < 3e-7) break;
            }
            return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }

        private static double GammaInc(double a, double x)
        {
            if (x <= 0) return 0;
            return x < a + 1.0 ? GammaIncSeries(a, x) : 1.0 - GammaIncCF(a, x);
        }

        private static double ChiSquarePValue(double x, double df)
            => x <= 0 ? 1.0 : 1.0 - GammaInc(df / 2.0, x / 2.0);

        private static double TDistPValue(double t, double df)
            => BetaInc(df / 2.0, 0.5, df / (df + t * t));
    }
}
